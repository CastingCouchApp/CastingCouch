using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Modules.Overlay.Extensions;

namespace CreatorControlSuite.Tests;

public sealed class OverlayExtensionPacksPageViewModelTests
{
    [Fact]
    public void Refresh_MapsCatalogAndStatus()
    {
        var store = new FakeExtensionStore();
        store.Catalog.Add(CreatePack("pack-a", "Pack A"));
        var viewModel = new OverlayExtensionPacksPageViewModel(store);

        viewModel.Refresh();

        OverlayExtensionPackItem item = Assert.Single(viewModel.Packs);
        Assert.Equal("pack-a", item.Pack.Id);
        Assert.Contains("Pack A", item.DisplayText);
        Assert.Contains("1 Extension Pack", viewModel.Status);
    }

    [Fact]
    public async Task ImportCommand_InstallsAndRefreshesCatalog()
    {
        var store = new FakeExtensionStore();
        var viewModel = new OverlayExtensionPacksPageViewModel(store)
        {
            OpenPackRequestedAsync =
                () => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]))
        };

        viewModel.ImportCommand.Execute(null);
        await WaitUntilAsync(() => store.InstallCount == 1);

        Assert.Single(viewModel.Packs);
        Assert.Contains("installiert", viewModel.Status);
    }

    [Fact]
    public async Task UninstallCommand_RequiresSelectionAndConfirmation()
    {
        var store = new FakeExtensionStore();
        store.Catalog.Add(CreatePack("pack-a", "Pack A"));
        var viewModel = new OverlayExtensionPacksPageViewModel(store);
        viewModel.Refresh();
        viewModel.SelectedPack = viewModel.Packs[0];
        viewModel.ConfirmUninstallRequestedAsync =
            _ => Task.FromResult(false);

        viewModel.UninstallCommand.Execute(null);
        await Task.Delay(25);
        Assert.Equal(0, store.UninstallCount);

        viewModel.ConfirmUninstallRequestedAsync =
            _ => Task.FromResult(true);
        viewModel.UninstallCommand.Execute(null);
        await WaitUntilAsync(() => store.UninstallCount == 1);

        Assert.Empty(viewModel.Packs);
    }

    [Fact]
    public async Task ImportCommand_ReportsFileSelectionErrors()
    {
        var viewModel = new OverlayExtensionPacksPageViewModel(
            new FakeExtensionStore())
        {
            OpenPackRequestedAsync = () =>
                Task.FromException<Stream?>(
                    new IOException("file locked"))
        };
        string? reported = null;
        viewModel.ErrorRequested =
            (message, _) => reported = message;

        viewModel.ImportCommand.Execute(null);
        await WaitUntilAsync(() => reported is not null);

        Assert.Contains("file locked", viewModel.Status);
        Assert.Equal(viewModel.Status, reported);
    }

    private static OverlayExtensionPackSummary CreatePack(
        string id,
        string name) =>
        new()
        {
            Id = id,
            Name = name,
            Version = "1.0.0",
            ApiVersion = 1
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 30 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FakeExtensionStore : IOverlayExtensionStore
    {
        public List<OverlayExtensionPackSummary> Catalog { get; } = [];
        public int InstallCount { get; private set; }
        public int UninstallCount { get; private set; }
        public string RootPath => "";

        public Task<OverlayExtensionPackSummary> InstallFromZipAsync(
            Stream zipStream,
            CancellationToken cancellationToken = default)
        {
            InstallCount++;
            OverlayExtensionPackSummary pack =
                CreatePack("imported", "Imported");
            Catalog.Add(pack);
            return Task.FromResult(pack);
        }

        public Task UninstallAsync(
            string packId,
            CancellationToken cancellationToken = default)
        {
            UninstallCount++;
            Catalog.RemoveAll(pack => pack.Id == packId);
            return Task.CompletedTask;
        }

        public IReadOnlyList<OverlayExtensionPackSummary> ListCatalog() =>
            Catalog;

        public bool TryResolveFile(
            string packId,
            string relativePath,
            out string fullPath)
        {
            fullPath = "";
            return false;
        }
    }
}
