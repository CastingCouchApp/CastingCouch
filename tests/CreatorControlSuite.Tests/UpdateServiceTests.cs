using System.Security.Cryptography;
using System.Text;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.Tests;

public sealed class ProductVersionInfoTests
{
    [Theory]
    [InlineData("8.0.0-alpha101", 8, 0, 0, "alpha", 101)]
    [InlineData("8.0.0", 8, 0, 0, null, 0)]
    [InlineData("1.2.3-beta2", 1, 2, 3, "beta", 2)]
    public void TryParse_ReadsSemVer(
        string input,
        int major,
        int minor,
        int patch,
        string? label,
        int preNumber)
    {
        Assert.True(ProductVersionInfo.TryParse(input, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(label, version.PreReleaseLabel);
        Assert.Equal(preNumber, version.PreReleaseNumber);
    }

    [Fact]
    public void ToMsiVersion_MapsAlphaPatch()
    {
        var version = ProductVersionInfo.Parse("8.0.0-alpha101");
        Assert.Equal("8.0.101", version.ToMsiVersion());
    }

    [Fact]
    public void Compare_PrefersNewerPrereleaseAndStable()
    {
        var older = ProductVersionInfo.Parse("8.0.0-alpha100");
        var newer = ProductVersionInfo.Parse("8.0.0-alpha101");
        var stable = ProductVersionInfo.Parse("8.0.0");

        Assert.True(newer > older);
        Assert.True(stable > newer);
    }
}

public sealed class UpdateManifestSignatureTests
{
    [Fact]
    public void RoundTrip_SignsAndVerifiesManifest()
    {
        using var rsa = RSA.Create(2048);
        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();

        var directory = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var publicKeyPath = Path.Combine(directory, "update-public.pem");
            File.WriteAllText(publicKeyPath, publicPem);

            var unsigned = new SignedUpdateManifest(
                UpdateManifestCanonical.ProductId,
                "8.0.0-alpha102",
                "Alpha",
                "CreatorControlSuite-8.0.0-alpha102-win-x64.zip",
                "ABC123",
                42,
                DateTimeOffset.Parse("2026-07-26T10:00:00Z"),
                "0.0.0",
                "Test notes",
                string.Empty);

            var payload = Encoding.UTF8.GetBytes(
                UpdateManifestCanonical.GetPayload(unsigned));

            using var signRsa = RSA.Create();
            signRsa.ImportFromPem(privatePem);
            var signature = Convert.ToBase64String(
                signRsa.SignData(
                    payload,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));

            var signed = unsigned with { Signature = signature };
            var verifier = new RsaUpdateSignatureVerifier(publicKeyPath);

            Assert.True(verifier.VerifyManifest(signed));

            var tampered = signed with { Version = "9.0.0" };
            Assert.False(verifier.VerifyManifest(tampered));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CanonicalPayload_IsStable()
    {
        var publishedAt = DateTimeOffset.Parse(
            "2026-07-26T12:00:00.0000000+00:00",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

        var manifest = new SignedUpdateManifest(
            "CreatorControlSuite",
            "8.0.0-alpha101",
            "Alpha",
            "pkg.zip",
            "DEADBEEF",
            100,
            publishedAt,
            "0.0.0",
            "line1\r\nline2",
            "sig");

        var payload = UpdateManifestCanonical.GetPayload(manifest);
        var expectedPublished = publishedAt.ToUniversalTime()
            .ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(
            "CreatorControlSuite\n8.0.0-alpha101\nAlpha\npkg.zip\nDEADBEEF\n100\n" +
            expectedPublished +
            "\n0.0.0\nline1\nline2",
            payload);
    }
}

public sealed class UpdateChannelSelectionTests
{
    [Fact]
    public void SelectRelease_PrefersMatchingChannel()
    {
        var releases = new List<LocalUpdateService.GitHubRelease>
        {
            new()
            {
                TagName = "v8.0.0-alpha101",
                Name = "Alpha",
                Prerelease = true,
                Assets = []
            },
            new()
            {
                TagName = "v8.0.0-beta1",
                Name = "Beta",
                Prerelease = true,
                Assets = []
            },
            new()
            {
                TagName = "v8.0.0",
                Name = "Stable",
                Prerelease = false,
                Assets = []
            }
        };

        var alpha = LocalUpdateService.SelectRelease(releases, "Alpha");
        Assert.Equal("v8.0.0-alpha101", alpha?.TagName);

        var beta = LocalUpdateService.SelectRelease(releases, "Beta");
        Assert.Equal("v8.0.0-beta1", beta?.TagName);

        var stable = LocalUpdateService.SelectRelease(releases, "Stable");
        Assert.Equal("v8.0.0", stable?.TagName);
    }
}
