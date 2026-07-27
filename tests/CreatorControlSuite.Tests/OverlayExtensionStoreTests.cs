using System.IO.Compression;
using CreatorControlSuite.Modules.Overlay.Extensions;

namespace CreatorControlSuite.Tests;

public sealed class OverlayExtensionStoreTests
{
    private const string ValidManifestJson = """
        {
          "id": "zip-test",
          "name": "Zip Test",
          "version": "1.0.0",
          "apiVersion": 1
        }
        """;

    private static string FixtureDir => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "overlay-pack",
        "cool-kit");

    [Fact]
    public async Task InstallFromZipAsync_ValidFixturePack_InstallsAndReturnsSummary()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = ZipDirectory(FixtureDir);

            OverlayExtensionPackSummary summary = await store.InstallFromZipAsync(zip);

            Assert.Equal("cool-kit", summary.Id);
            Assert.Equal("Cool Kit", summary.Name);
            Assert.Equal("1.0.0", summary.Version);
            Assert.Equal(1, summary.ApiVersion);
            Assert.Single(summary.Widgets);
            Assert.Equal("banner", summary.Widgets[0].Id);
            Assert.Single(summary.Effects);
            Assert.Equal("sparkle", summary.Effects[0].Id);
            Assert.Single(summary.Fonts);
            Assert.Equal("CoolFont", summary.Fonts[0].Family);
            Assert.Contains("assets/icons/logo.svg", summary.Assets);

            string packDir = Path.Combine(root, "cool-kit");
            Assert.True(File.Exists(Path.Combine(packDir, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(packDir, "widgets", "banner", "index.js")));
            Assert.True(File.Exists(Path.Combine(packDir, "effects", "sparkle", "index.js")));
            Assert.True(File.Exists(Path.Combine(packDir, "fonts", "CoolFont.woff2")));
            Assert.True(File.Exists(Path.Combine(packDir, "assets", "icons", "logo.svg")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task InstallFromZipAsync_RejectsZipSlip()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = BuildZip(archive =>
            {
                AddEntry(archive, "manifest.json", ValidManifestJson);
                AddEntry(archive, "../evil.js", "alert('pwned');");
            });

            await Assert.ThrowsAsync<OverlayExtensionValidationException>(() => store.InstallFromZipAsync(zip));
            Assert.Empty(store.ListCatalog());
            Assert.False(File.Exists(Path.Combine(root, "..", "evil.js")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task InstallFromZipAsync_RejectsZipSlipWithBackslashes()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = BuildZip(archive =>
            {
                AddEntry(archive, "manifest.json", ValidManifestJson);
                AddEntry(archive, "..\\..\\evil.js", "alert('pwned');");
            });

            await Assert.ThrowsAsync<OverlayExtensionValidationException>(() => store.InstallFromZipAsync(zip));
            Assert.Empty(store.ListCatalog());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task InstallFromZipAsync_RejectsDisallowedExtension()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = BuildZip(archive =>
            {
                AddEntry(archive, "manifest.json", ValidManifestJson);
                AddEntry(archive, "payload.exe", "MZ-not-really-an-exe");
            });

            await Assert.ThrowsAsync<OverlayExtensionValidationException>(() => store.InstallFromZipAsync(zip));
            Assert.Empty(store.ListCatalog());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task InstallFromZipAsync_RejectsMissingManifest()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = BuildZip(archive =>
            {
                AddEntry(archive, "widgets/banner/index.js", "// no manifest here");
            });

            await Assert.ThrowsAsync<OverlayExtensionValidationException>(() => store.InstallFromZipAsync(zip));
            Assert.Empty(store.ListCatalog());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task InstallFromZipAsync_RejectsMalformedManifestJson()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = BuildZip(archive =>
            {
                AddEntry(archive, "manifest.json", "{ this is not valid json");
            });

            await Assert.ThrowsAsync<OverlayExtensionValidationException>(() => store.InstallFromZipAsync(zip));
            Assert.Empty(store.ListCatalog());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Theory]
    [InlineData("""{"name":"No Id","version":"1.0.0","apiVersion":1}""")]
    [InlineData("""{"id":"Bad ID!","name":"Bad Id","version":"1.0.0","apiVersion":1}""")]
    [InlineData("""{"id":"no-version","name":"No Version","apiVersion":1}""")]
    [InlineData("""{"id":"no-api-version","name":"No Api Version","version":"1.0.0"}""")]
    [InlineData("""{"id":"wrong-api-version","name":"Wrong Api Version","version":"1.0.0","apiVersion":2}""")]
    public async Task InstallFromZipAsync_RejectsInvalidManifestSchema(string manifestJson)
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = BuildZip(archive => AddEntry(archive, "manifest.json", manifestJson));

            await Assert.ThrowsAsync<OverlayExtensionValidationException>(() => store.InstallFromZipAsync(zip));
            Assert.Empty(store.ListCatalog());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task InstallFromZipAsync_ReplacesExistingPackWithSameId()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream first = ZipDirectory(FixtureDir);
            await store.InstallFromZipAsync(first);

            using MemoryStream second = BuildZip(archive =>
            {
                AddEntry(archive, "manifest.json", """
                    {
                      "id": "cool-kit",
                      "name": "Cool Kit v2",
                      "version": "2.0.0",
                      "apiVersion": 1
                    }
                    """);
            });
            OverlayExtensionPackSummary summary = await store.InstallFromZipAsync(second);

            Assert.Equal("Cool Kit v2", summary.Name);
            Assert.Single(store.ListCatalog());
            Assert.False(File.Exists(Path.Combine(root, "cool-kit", "widgets", "banner", "index.js")));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ListCatalog_ReturnsInstalledPack()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = ZipDirectory(FixtureDir);
            await store.InstallFromZipAsync(zip);

            IReadOnlyList<OverlayExtensionPackSummary> catalog = store.ListCatalog();

            Assert.Single(catalog);
            Assert.Equal("cool-kit", catalog[0].Id);
            Assert.Equal("Cool Kit", catalog[0].Name);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void ListCatalog_EmptyRoot_ReturnsEmpty()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            Assert.Empty(store.ListCatalog());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task TryResolveFile_ResolvesFileWithinPack()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = ZipDirectory(FixtureDir);
            await store.InstallFromZipAsync(zip);

            Assert.True(store.TryResolveFile("cool-kit", "widgets/banner/index.js", out string fullPath));
            Assert.True(File.Exists(fullPath));

            Assert.True(store.TryResolveFile("cool-kit", "manifest.json", out string manifestPath));
            Assert.True(File.Exists(manifestPath));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task TryResolveFile_RejectsTraversalAndUnknownTargets()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = ZipDirectory(FixtureDir);
            await store.InstallFromZipAsync(zip);

            Assert.False(store.TryResolveFile("cool-kit", "../outside.txt", out string traversal));
            Assert.Equal("", traversal);

            Assert.False(store.TryResolveFile("cool-kit", "..\\..\\evil.js", out _));
            Assert.False(store.TryResolveFile("unknown-pack", "manifest.json", out _));
            Assert.False(store.TryResolveFile("cool-kit", "does-not-exist.js", out _));
            Assert.False(store.TryResolveFile("../evil", "manifest.json", out _));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task UninstallAsync_RemovesPack()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            using MemoryStream zip = ZipDirectory(FixtureDir);
            await store.InstallFromZipAsync(zip);
            Assert.True(Directory.Exists(Path.Combine(root, "cool-kit")));

            await store.UninstallAsync("cool-kit");

            Assert.False(Directory.Exists(Path.Combine(root, "cool-kit")));
            Assert.Empty(store.ListCatalog());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task UninstallAsync_RejectsInvalidPackId()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayExtensionStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() => store.UninstallAsync("../evil"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateTempRoot() =>
        Path.Combine(Path.GetTempPath(), "ccs-extensions-" + Guid.NewGuid().ToString("N"));

    private static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static MemoryStream ZipDirectory(string sourceDir)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, relative);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildZip(Action<ZipArchive> configure)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            configure(archive);
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream);
        writer.Write(content);
    }
}
