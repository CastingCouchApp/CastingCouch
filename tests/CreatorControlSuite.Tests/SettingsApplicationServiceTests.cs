using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Validation;

namespace CreatorControlSuite.Tests;

public sealed class SettingsApplicationServiceTests
{
    [Fact]
    public async Task LoadAsync_NormalizesAndPersistsLegacySettings()
    {
        var settings = new AppSettings
        {
            Workflow =
            {
                TimedAutomations = null!,
                RunOfShowSteps = null!,
                RunOfShowPlans = null!
            },
            Dashboard = { SceneButtons = null! },
            Obs = { AudioProfiles = null! },
            Alerts = { AutoCreateObsSources = true },
            Twitch = { Scopes = ["user:read:chat"] },
            Overlay = { Canvases = [] }
        };
        var store = new RecordingSettingsStore(settings);
        var service = new SettingsApplicationService(
            store,
            new StubValidator(isValid: true));

        AppSettings loaded = await service.LoadAsync();

        Assert.NotNull(loaded.Workflow.TimedAutomations);
        Assert.NotNull(loaded.Workflow.RunOfShowSteps);
        Assert.NotNull(loaded.Workflow.RunOfShowPlans);
        Assert.NotNull(loaded.Dashboard.SceneButtons);
        Assert.NotNull(loaded.Obs.AudioProfiles);
        Assert.False(loaded.Alerts.AutoCreateObsSources);
        Assert.Contains("channel:read:guest_star", loaded.Twitch.Scopes);
        Assert.Contains(
            "moderator:manage:banned_users",
            loaded.Twitch.Scopes);
        Assert.Single(loaded.Overlay.Canvases);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistInvalidSettings()
    {
        var store = new RecordingSettingsStore(new AppSettings());
        ValidationIssue issue = new(
            "INVALID",
            ValidationSeverity.Error,
            "Test",
            "Ungültig",
            "Korrigieren");
        var service = new SettingsApplicationService(
            store,
            new StubValidator(isValid: false, issue));

        ValidationReport report = await service.SaveAsync(new AppSettings());

        Assert.False(report.IsValid);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task SaveAsync_PersistsValidSettings()
    {
        var store = new RecordingSettingsStore(new AppSettings());
        var service = new SettingsApplicationService(
            store,
            new StubValidator(isValid: true));

        ValidationReport report = await service.SaveAsync(new AppSettings());

        Assert.True(report.IsValid);
        Assert.Equal(1, store.SaveCount);
    }

    private sealed class RecordingSettingsStore(
        AppSettings settings) : ISettingsStore
    {
        public int SaveCount { get; private set; }

        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(
            AppSettings value,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubValidator(
        bool isValid,
        params ValidationIssue[] issues) : ISettingsValidator
    {
        public ValidationReport Validate(AppSettings settings) =>
            new(isValid, issues);
    }
}
