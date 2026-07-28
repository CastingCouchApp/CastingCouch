using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Tests;

public sealed class OverlayCanvasApplicationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ccs-overlay-canvas-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Create_PersistsDefaultLayoutAndSelectsCanvas()
    {
        var settings = new AppSettings();
        settings.Overlay.EnsureCanvasesMigrated();
        var store = new FakeSettingsStore(settings);
        var layouts = new OverlayLayoutStore(_root);
        var webServer = new FakeOverlayWebServer
        {
            IsRunningValue = true
        };
        var service = CreateService(store, layouts, webServer);

        OverlayCanvasSettings canvas =
            await service.CreateAsync(settings, " My Canvas ");

        Assert.Equal("my-canvas", canvas.Id);
        Assert.Equal("My Canvas", canvas.Name);
        Assert.Equal(canvas.Id, settings.Overlay.SelectedCanvasId);
        Assert.True(layouts.Exists(canvas.Id));
        OverlayLayout layout = await layouts.LoadAsync(canvas.Id);
        Assert.Equal("My Canvas", layout.Name);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, webServer.RefreshCount);
    }

    [Fact]
    public async Task Duplicate_CopiesLayoutAndUsesUniqueId()
    {
        var settings = new AppSettings();
        settings.Overlay.Canvases =
        [
            new OverlayCanvasSettings
            {
                Id = "source",
                Name = "Source"
            },
            new OverlayCanvasSettings
            {
                Id = "source-kopie",
                Name = "Existing"
            }
        ];
        settings.Overlay.SelectedCanvasId = "source";
        var store = new FakeSettingsStore(settings);
        var layouts = new OverlayLayoutStore(_root);
        var sourceLayout = OverlayLayout.CreateDefault();
        sourceLayout.Name = "Source";
        sourceLayout.Items.Add(new OverlayLayoutItem
        {
            Id = "contract-item",
            Type = "text"
        });
        await layouts.SaveAsync("source", sourceLayout);
        var service = CreateService(
            store,
            layouts,
            new FakeOverlayWebServer());

        OverlayCanvasSettings duplicate =
            await service.DuplicateAsync(
                settings,
                "source",
                "Source Kopie");

        Assert.Equal("source-kopie-2", duplicate.Id);
        Assert.Equal("Source Kopie", duplicate.Name);
        OverlayLayout copied = await layouts.LoadAsync(duplicate.Id);
        Assert.Equal("Source Kopie", copied.Name);
        Assert.Contains(
            copied.Items,
            item => item.Id == "contract-item");
    }

    [Fact]
    public async Task Delete_PersistsMetadataBeforeRemovingLayout()
    {
        var settings = CreateTwoCanvasSettings();
        var events = new List<string>();
        var store = new FakeSettingsStore(settings, events);
        var layouts = new TrackingLayoutStore(events);
        layouts.Layouts["second"] = OverlayLayout.CreateDefault();
        var service = CreateService(
            store,
            layouts,
            new FakeOverlayWebServer());

        await service.DeleteAsync(settings, "second");

        Assert.DoesNotContain(
            settings.Overlay.Canvases,
            canvas => canvas.Id == "second");
        Assert.Equal(["save", "delete:second"], events);
    }

    [Fact]
    public async Task Delete_RejectsLastCanvas()
    {
        var settings = new AppSettings();
        settings.Overlay.EnsureCanvasesMigrated();
        var service = CreateService(
            new FakeSettingsStore(settings),
            new OverlayLayoutStore(_root),
            new FakeOverlayWebServer());

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DeleteAsync(
                    settings,
                    settings.Overlay.Canvases[0].Id));

        Assert.Contains("letzte", exception.Message);
    }

    [Fact]
    public async Task Create_RollsBackMetadataAndLayoutWhenSettingsSaveFails()
    {
        var settings = new AppSettings();
        settings.Overlay.EnsureCanvasesMigrated();
        string originalSelected = settings.Overlay.SelectedCanvasId;
        var store = new FakeSettingsStore(settings)
        {
            SaveException = new IOException("disk full")
        };
        var layouts = new OverlayLayoutStore(_root);
        var service = CreateService(
            store,
            layouts,
            new FakeOverlayWebServer());

        await Assert.ThrowsAsync<IOException>(
            () => service.CreateAsync(settings, "Rollback"));

        Assert.DoesNotContain(
            settings.Overlay.Canvases,
            canvas => canvas.Id == "rollback");
        Assert.Equal(originalSelected, settings.Overlay.SelectedCanvasId);
        Assert.False(layouts.Exists("rollback"));
    }

    private static AppSettings CreateTwoCanvasSettings()
    {
        var settings = new AppSettings();
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

    private static OverlayCanvasApplicationService CreateService(
        ISettingsStore settings,
        IOverlayLayoutStore layouts,
        IOverlayWebServer webServer) =>
        new(settings, layouts, webServer, new NullLogger());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeSettingsStore(
        AppSettings settings,
        List<string>? events = null) : ISettingsStore
    {
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; init; }
        public List<string> Events { get; } = events ?? [];

        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(
            AppSettings value,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Events.Add("save");
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }
    }

    private sealed class FakeOverlayWebServer : IOverlayWebServer
    {
        public bool IsRunningValue { get; init; }
        public int RefreshCount { get; private set; }
        public bool IsRunning => IsRunningValue;
        public int Port => 8765;
        public string? BaseUrl => "http://127.0.0.1:8765";
        public string? RootPath => null;

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RestartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RefreshMountedCanvasesAsync(
            CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingLayoutStore(
        List<string>? events = null) : IOverlayLayoutStore
    {
        public Dictionary<string, OverlayLayout> Layouts { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> Events { get; } = events ?? [];

        public Task<OverlayLayout> LoadAsync(
            string instanceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Layouts.TryGetValue(instanceId, out OverlayLayout? layout)
                ? layout
                : OverlayLayout.CreateDefault());

        public Task SaveAsync(
            string instanceId,
            OverlayLayout layout,
            CancellationToken cancellationToken = default)
        {
            Layouts[instanceId] = layout;
            return Task.CompletedTask;
        }

        public string GetLayoutFilePath(string instanceId) => instanceId;
        public bool Exists(string instanceId) => Layouts.ContainsKey(instanceId);
        public IReadOnlyList<string> ListInstanceIds() => [.. Layouts.Keys];

        public Task DeleteAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            Events.Add("delete:" + instanceId);
            Layouts.Remove(instanceId);
            return Task.CompletedTask;
        }

        public Task DuplicateAsync(
            string sourceId,
            string targetId,
            CancellationToken cancellationToken = default)
        {
            Layouts[targetId] = Layouts[sourceId];
            return Task.CompletedTask;
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public event EventHandler<AppLogEntry>? EntryWritten
        {
            add { }
            remove { }
        }

        public void Write(
            AppLogLevel level,
            string category,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? properties = null)
        {
        }

        public Task<IReadOnlyList<AppLogEntry>> ReadRecentAsync(
            int maxEntries = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppLogEntry>>([]);

        public Task<string> ExportAsync(
            string destinationDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("");
    }
}
