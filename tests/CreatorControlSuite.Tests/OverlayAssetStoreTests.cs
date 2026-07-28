using CreatorControlSuite.Modules.Overlay.Assets;

namespace CreatorControlSuite.Tests;

public sealed class OverlayAssetStoreTests
{
    [Fact]
    public async Task ImportAsync_CopiesFileAndListsIt()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            byte[] png = MinimalPng();

            OverlayAssetInfo asset;
            await using (var stream = new MemoryStream(png))
            {
                asset = await store.ImportAsync(stream, "logo.png");
            }

            Assert.False(string.IsNullOrWhiteSpace(asset.Id));
            Assert.Equal("logo.png", asset.OriginalName);
            Assert.Equal("image/png", asset.ContentType);
            Assert.Equal(png.Length, asset.SizeBytes);
            Assert.Equal("/assets/" + asset.Id, asset.PublicUrl);
            Assert.True(File.Exists(asset.LocalPath));
            Assert.Equal(png, await File.ReadAllBytesAsync(asset.LocalPath));

            IReadOnlyList<OverlayAssetInfo> listed = store.List();
            Assert.Single(listed);
            Assert.Equal(asset.Id, listed[0].Id);
            Assert.True(store.TryGet(asset.Id, out OverlayAssetInfo found));
            Assert.Equal(asset.LocalPath, found.LocalPath);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ImportAsync_RejectsDisallowedExtension()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            await using var stream = new MemoryStream([1, 2, 3, 4]);

            OverlayAssetValidationException ex = await Assert.ThrowsAsync<OverlayAssetValidationException>(
                () => store.ImportAsync(stream, "payload.exe"));

            Assert.Contains("Dateityp", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(store.List());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ImportAsync_RejectsOversizedFile()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            byte[] huge = new byte[OverlayAssetStore.MaxAssetSizeBytes + 1];
            await using var stream = new MemoryStream(huge);

            await Assert.ThrowsAsync<OverlayAssetValidationException>(
                () => store.ImportAsync(stream, "big.png"));
            Assert.Empty(store.List());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndIndexEntry()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            OverlayAssetInfo asset;
            await using (var stream = new MemoryStream(MinimalPng()))
            {
                asset = await store.ImportAsync(stream, "a.webp");
            }

            await store.DeleteAsync(asset.Id);

            Assert.Empty(store.List());
            Assert.False(store.TryGet(asset.Id, out _));
            Assert.False(File.Exists(asset.LocalPath));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task List_ReloadsFromIndexOnNewStoreInstance()
    {
        string root = CreateTempRoot();
        try
        {
            OverlayAssetInfo asset;
            await using (var stream = new MemoryStream(MinimalJpeg()))
            {
                asset = await new OverlayAssetStore(root).ImportAsync(stream, "photo.jpg");
            }

            var reloaded = new OverlayAssetStore(root);
            Assert.True(reloaded.TryGet(asset.Id, out OverlayAssetInfo found));
            Assert.Equal("photo.jpg", found.OriginalName);
            Assert.Equal("image/jpeg", found.ContentType);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

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
            // best-effort
        }
    }

    // 1x1 PNG
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE, 0xD4, 0xEF, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    private static byte[] MinimalJpeg() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];
}
