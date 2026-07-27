using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.Tests;

public sealed class AlertEngineTests
{
    [Fact]
    public async Task Start_Enqueue_PlaysAlert()
    {
        await using var harness = CreateHarness();
        await harness.Engine.StartAsync();

        await harness.Engine.EnqueueAsync(
            new AlertRequest(
                Guid.NewGuid(),
                "Follow",
                "Tester",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.Now,
                100));

        await WaitUntilAsync(() => harness.Renderer.ShowCalls >= 1, TimeSpan.FromSeconds(5));

        Assert.True(harness.Renderer.ShowCalls >= 1);
        Assert.Equal("Follow", harness.Renderer.LastShownType);
        Assert.Equal("Tester folgt jetzt!", harness.Renderer.LastShownText);
    }

    [Fact]
    public async Task ClearQueue_Empties()
    {
        await using var harness = CreateHarness(settings =>
        {
            settings.Alerts.Definitions["Follow"].DurationSeconds = 5;
        });
        await harness.Engine.StartAsync();

        for (var i = 0; i < 2; i++)
        {
            await harness.Engine.EnqueueAsync(
                new AlertRequest(
                    Guid.NewGuid(),
                    "Follow",
                    "User" + i,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    DateTimeOffset.Now,
                    100));
        }

        await harness.Engine.ClearQueueAsync();

        Assert.Equal(0, harness.Engine.State.QueueLength);
    }

    [Fact]
    public async Task BuildPreviewAsync_RendersTemplate()
    {
        await using var harness = CreateHarness();

        var preview = await harness.Engine.BuildPreviewAsync("Follow", "Alice");

        Assert.Equal("Follow", preview.Type);
        Assert.Equal("Alice folgt jetzt!", preview.Text);
        Assert.Equal(TimeSpan.FromSeconds(1), preview.Duration);
    }

    [Fact]
    public async Task DropOldest_WhenQueueFull()
    {
        await using var harness = CreateHarness();
        await harness.Engine.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            await harness.Engine.EnqueueAsync(
                new AlertRequest(
                    Guid.NewGuid(),
                    "Follow",
                    "User" + i,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    DateTimeOffset.Now,
                    100));
        }

        await harness.Engine.ClearQueueAsync();

        Assert.Equal(0, harness.Engine.State.QueueLength);
    }

    [Fact]
    public async Task StopAsync_HidesRenderer()
    {
        await using var harness = CreateHarness();
        await harness.Engine.StartAsync();

        await harness.Engine.EnqueueAsync(
            new AlertRequest(
                Guid.NewGuid(),
                "Follow",
                "StopMe",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.Now,
                100));

        await WaitUntilAsync(() => harness.Renderer.ShowCalls >= 1, TimeSpan.FromSeconds(5));
        await harness.Engine.StopAsync();

        Assert.True(harness.Renderer.HideCalls >= 1);
        Assert.Equal("Gestoppt", harness.Engine.State.Detail);
    }

    private static Harness CreateHarness(Action<AppSettings>? configure = null)
    {
        var settings = new AppSettings();
        settings.Alerts.QueueCapacity = 2;
        settings.Alerts.InterAlertDelayMilliseconds = 0;
        settings.Alerts.Definitions["Follow"].DurationSeconds = 1;
        settings.Alerts.Definitions["Follow"].TextTemplate = "{user} folgt jetzt!";
        configure?.Invoke(settings);

        var store = new InMemorySettingsStore(settings);
        var renderer = new FakeAlertRenderer();
        var engine = new AlertEngine(
            store,
            new AlertDefinitionProvider(store),
            renderer);

        return new Harness(engine, renderer);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition(), "Bedingung innerhalb des Timeouts nicht erfüllt.");
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(AlertEngine engine, FakeAlertRenderer renderer)
        {
            Engine = engine;
            Renderer = renderer;
        }

        public AlertEngine Engine { get; }
        public FakeAlertRenderer Renderer { get; }

        public ValueTask DisposeAsync() => Engine.DisposeAsync();
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private AppSettings _settings;

        public InMemorySettingsStore(AppSettings settings) => _settings = settings;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAlertRenderer : IAlertRenderer
    {
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }
        public string? LastShownType { get; private set; }
        public string? LastShownText { get; private set; }

        public Task InstallSourcesAsync(
            AlertDefinition definition,
            string renderedText,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ShowAsync(
            AlertDefinition definition,
            string renderedText,
            CancellationToken cancellationToken = default)
        {
            ShowCalls++;
            LastShownType = definition.Type;
            LastShownText = renderedText;
            return Task.CompletedTask;
        }

        public Task HideAsync(CancellationToken cancellationToken = default)
        {
            HideCalls++;
            return Task.CompletedTask;
        }
    }
}
