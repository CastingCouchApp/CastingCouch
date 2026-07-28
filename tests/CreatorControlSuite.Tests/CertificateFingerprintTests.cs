using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Tests;

public sealed class CertificateFingerprintTests
{
    [Theory]
    [InlineData(':')]
    [InlineData(' ')]
    [InlineData('-')]
    public void Normalize_AcceptsHumanReadableSeparators(char separator)
    {
        const string expected =
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        string value = string.Join(separator, Enumerable.Range(0, 32)
            .Select(index => expected.Substring(index * 2, 2).ToLowerInvariant()));

        Assert.Equal(expected, CertificateFingerprint.Normalize(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("ABC")]
    [InlineData("AA/BB")]
    public void Normalize_RejectsMalformedValues(string value)
    {
        Assert.Throws<FormatException>(() => CertificateFingerprint.Normalize(value));
    }

    [Fact]
    public void Matches_UsesNormalizedSha256Fingerprint()
    {
        const string fingerprint =
            "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

        Assert.True(CertificateFingerprint.Matches(
            fingerprint,
            "01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:" +
            "01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF"));
        Assert.False(CertificateFingerprint.Matches(
            fingerprint,
            new string('F', 64)));
    }
}
