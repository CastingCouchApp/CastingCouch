using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class OverlayCanvasPageViewModelTests
{
    [Fact]
    public void Load_MapsSelectionUrlsAndWidgetCatalog()
    {
        var service = new FakeCanvasService();
        var viewModel = new OverlayCanvasPageViewModel(service);
        AppSettings settings = CreateSettings();

        viewModel.Load(settings);

        Assert.Equal("second", viewModel.SelectedCanvas?.Id);
        Assert.Equal(
            "http://127.0.0.1:8765/view/second",
            viewModel.ViewUrl);
        Assert.Equal(
            "http://127.0.0.1:8765/editor/second",
            viewModel.EditorUrl);
        Assert.Contains(
            viewModel.WidgetUrls,
            item => item.Path == "music");
        Assert.NotNull(viewModel.SelectedWidget);
    }

    [Fact]
    public async Task CreateCommand_PromptsDelegatesAndSelectsResult()
    {
        var service = new FakeCanvasService();
        var viewModel = new OverlayCanvasPageViewModel(service);
        AppSettings settings = CreateSettings();
        viewModel.Load(settings);
        viewModel.PromptNameRequestedAsync =
            request => Task.FromResult<string?>("Created");

        viewModel.CreateCommand.Execute(null);
        await WaitUntilAsync(
            () => service.LastOperation == "create:Created");

        Assert.Equal("created", viewModel.SelectedCanvas?.Id);
        Assert.Contains("angelegt", viewModel.Status);
    }

    [Fact]
    public async Task DeleteCommand_RequiresConfirmation()
    {
        var service = new FakeCanvasService();
        var viewModel = new OverlayCanvasPageViewModel(service);
        AppSettings settings = CreateSettings();
        viewModel.Load(settings);
        viewModel.ConfirmDeleteRequestedAsync =
            _ => Task.FromResult(false);

        viewModel.DeleteCommand.Execute(null);
        await Task.Delay(25);
        Assert.Null(service.LastOperation);

        viewModel.ConfirmDeleteRequestedAsync =
            _ => Task.FromResult(true);
        viewModel.DeleteCommand.Execute(null);
        await WaitUntilAsync(
            () => service.LastOperation?.StartsWith(
                "delete:",
                StringComparison.Ordinal) == true);

        Assert.DoesNotContain(
            viewModel.Canvases,
            canvas => canvas.Id == "second");
    }

    [Fact]
    public void CopyWidgetCommand_UsesSelectedWidgetUrl()
    {
        var viewModel = new OverlayCanvasPageViewModel(
            new FakeCanvasService());
        viewModel.Load(CreateSettings());
        viewModel.SelectedWidget = viewModel.WidgetUrls.First(
            item => item.Path == "shape/frame");
        string? copied = null;
        viewModel.CopyTextRequested = text => copied = text;

        viewModel.CopyWidgetUrlCommand.Execute(null);

        Assert.Equal(
            "http://127.0.0.1:8765/w/shape/frame",
            copied);
    }

    private static AppSettings CreateSettings()
    {
        var settings = new AppSettings();
        settings.Overlay.WebServerPort = 8765;
        settings.Overlay.Canvases =
        [
            new OverlayCanvasSettings
            {
                Id = "first",
                Name = "First"
            },
            new OverlayCanvasSettings
            {
                Id = "second",
                Name = "Second"
            }
        ];
        settings.Overlay.SelectedCanvasId = "second";
        return settings;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 30 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FakeCanvasService :
        IOverlayCanvasApplicationService
    {
        public string? LastOperation { get; private set; }

        public Task<OverlayCanvasSettings> CreateAsync(
            AppSettings settings,
            string name,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "create:" + name;
            var canvas = new OverlayCanvasSettings
            {
                Id = name.ToLowerInvariant(),
                Name = name
            };
            settings.Overlay.Canvases.Add(canvas);
            settings.Overlay.SelectedCanvasId = canvas.Id;
            return Task.FromResult(canvas);
        }

        public Task<OverlayCanvasSettings> RenameAsync(
            AppSettings settings,
            string canvasId,
            string name,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "rename:" + canvasId;
            OverlayCanvasSettings canvas = settings.Overlay.Canvases
                .Single(item => item.Id == canvasId);
            canvas.Name = name;
            return Task.FromResult(canvas);
        }

        public Task<OverlayCanvasSettings> DuplicateAsync(
            AppSettings settings,
            string sourceId,
            string name,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "duplicate:" + sourceId;
            var canvas = new OverlayCanvasSettings
            {
                Id = "duplicate",
                Name = name
            };
            settings.Overlay.Canvases.Add(canvas);
            settings.Overlay.SelectedCanvasId = canvas.Id;
            return Task.FromResult(canvas);
        }

        public Task DeleteAsync(
            AppSettings settings,
            string canvasId,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "delete:" + canvasId;
            settings.Overlay.Canvases.RemoveAll(
                item => item.Id == canvasId);
            settings.Overlay.EnsureCanvasesMigrated();
            return Task.CompletedTask;
        }

        public Task SelectAsync(
            AppSettings settings,
            string canvasId,
            CancellationToken cancellationToken = default)
        {
            LastOperation = "select:" + canvasId;
            settings.Overlay.SelectedCanvasId = canvasId;
            return Task.CompletedTask;
        }
    }
}
