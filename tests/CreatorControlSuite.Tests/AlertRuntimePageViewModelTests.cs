using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.Tests;

public sealed class AlertRuntimePageViewModelTests
{
    [Fact]
    public void Load_MapsAlertAndStreamerBotSettings()
    {
        var alerts = new AlertSettings
        {
            Enabled = false,
            ObsSceneName = "alerts",
            ObsMediaSourceName = "media",
            ObsTextSourceName = "text",
            InterAlertDelayMilliseconds = 725
        };
        var streamerBot = new StreamerBotSettings
        {
            SuppressAlertActionsWhenSuiteAlertsEnabled = true,
            DisableAlertsActionName = "disable",
            DisableAlertsActionId = "disable-id",
            EnableAlertsActionName = "enable",
            EnableAlertsActionId = "enable-id"
        };
        var viewModel = new AlertRuntimePageViewModel();

        viewModel.Load(alerts, streamerBot);

        Assert.False(viewModel.Enabled);
        Assert.True(viewModel.SuppressStreamerBotAlerts);
        Assert.Equal("disable", viewModel.DisableActionName);
        Assert.Equal("disable-id", viewModel.DisableActionId);
        Assert.Equal("enable", viewModel.EnableActionName);
        Assert.Equal("enable-id", viewModel.EnableActionId);
        Assert.Equal("alerts", viewModel.ObsSceneName);
        Assert.Equal("media", viewModel.ObsMediaSourceName);
        Assert.Equal("text", viewModel.ObsTextSourceName);
        Assert.Equal("725", viewModel.InterAlertDelayMilliseconds);
    }

    [Fact]
    public void TryApplyTo_MapsAndNormalizesSettings()
    {
        var viewModel = new AlertRuntimePageViewModel
        {
            Enabled = true,
            SuppressStreamerBotAlerts = true,
            DisableActionName = " disable ",
            DisableActionId = " disable-id ",
            EnableActionName = " enable ",
            EnableActionId = " enable-id ",
            ObsSceneName = " alerts ",
            ObsMediaSourceName = " media ",
            ObsTextSourceName = " text ",
            InterAlertDelayMilliseconds = "900"
        };
        var alerts = new AlertSettings();
        var streamerBot = new StreamerBotSettings();

        bool applied = viewModel.TryApplyTo(
            alerts,
            streamerBot,
            out string error);

        Assert.True(applied, error);
        Assert.True(alerts.Enabled);
        Assert.Equal("alerts", alerts.ObsSceneName);
        Assert.Equal("media", alerts.ObsMediaSourceName);
        Assert.Equal("text", alerts.ObsTextSourceName);
        Assert.Equal(900, alerts.InterAlertDelayMilliseconds);
        Assert.True(streamerBot.SuppressAlertActionsWhenSuiteAlertsEnabled);
        Assert.Equal("disable", streamerBot.DisableAlertsActionName);
        Assert.Equal("disable-id", streamerBot.DisableAlertsActionId);
        Assert.Equal("enable", streamerBot.EnableAlertsActionName);
        Assert.Equal("enable-id", streamerBot.EnableAlertsActionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("invalid")]
    public void TryApplyTo_RejectsInvalidInterAlertDelay(string value)
    {
        var viewModel = new AlertRuntimePageViewModel
        {
            InterAlertDelayMilliseconds = value
        };

        bool applied = viewModel.TryApplyTo(
            new AlertSettings(),
            new StreamerBotSettings(),
            out string error);

        Assert.False(applied);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void UpdateQueueState_FormatsRunningAndIdleStates()
    {
        var viewModel = new AlertRuntimePageViewModel();
        var request = new AlertRequest(
            Guid.NewGuid(),
            "Raid",
            "raider",
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow,
            10);

        viewModel.UpdateQueueState(
            new AlertPlaybackState(
                true,
                request,
                2,
                DateTimeOffset.UtcNow,
                ""));

        Assert.Equal("Raid läuft · Queue: 2", viewModel.QueueStatus);

        viewModel.UpdateQueueState(
            new AlertPlaybackState(false, null, 0, null, ""));

        Assert.Equal("Bereit · Queue: 0", viewModel.QueueStatus);
    }

    [Fact]
    public void SelectActions_ResolvesCurrentIdsAndNames()
    {
        var viewModel = new AlertRuntimePageViewModel();
        viewModel.SetActions(
        [
            new("disable-id", "Disable", "Alerts", true),
            new("enable-id", "Enable", "Alerts", true)
        ]);

        viewModel.SelectActions(
            "disable-id",
            "old disable",
            "",
            "Enable");

        Assert.Equal("Disable", viewModel.DisableActionName);
        Assert.Equal("disable-id", viewModel.DisableActionId);
        Assert.Equal("Enable", viewModel.EnableActionName);
        Assert.Equal("enable-id", viewModel.EnableActionId);
    }

    [Fact]
    public void StatusMethods_MapExternalAdapterResults()
    {
        var viewModel = new AlertRuntimePageViewModel();

        viewModel.SetStreamerBotGroups(["Alerts", "System", "Alerts"]);
        viewModel.SetStreamerBotStatus("Verbunden");
        viewModel.SetInstallStatus("Installiert");

        Assert.Equal(
            "Gefundene Aktionsgruppen: Alerts, System",
            viewModel.StreamerBotGroups);
        Assert.Equal("Verbunden", viewModel.StreamerBotStatus);
        Assert.Equal("Installiert", viewModel.InstallStatus);
    }

    [Fact]
    public async Task Commands_DelegateToRuntimeAdapters()
    {
        var calls = new List<string>();
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new AlertRuntimePageViewModel
        {
            RefreshActionsRequestedAsync = () =>
            {
                calls.Add("refresh");
                return Task.CompletedTask;
            },
            ApplySuppressionRequestedAsync = () =>
            {
                calls.Add("suppress");
                return Task.CompletedTask;
            },
            SetStreamerBotAlertsRequestedAsync = enabled =>
            {
                calls.Add(enabled ? "enable" : "disable");
                return Task.CompletedTask;
            },
            StopCurrentAlertRequestedAsync = () =>
            {
                calls.Add("stop");
                return Task.CompletedTask;
            },
            ClearAlertQueueRequestedAsync = () =>
            {
                calls.Add("clear");
                return Task.CompletedTask;
            },
            InstallObsSourcesRequestedAsync = () =>
            {
                calls.Add("install");
                completed.SetResult();
                return Task.CompletedTask;
            }
        };

        viewModel.RefreshActionsCommand.Execute(null);
        viewModel.ApplySuppressionCommand.Execute(null);
        viewModel.DisableStreamerBotAlertsCommand.Execute(null);
        viewModel.EnableStreamerBotAlertsCommand.Execute(null);
        viewModel.StopCurrentAlertCommand.Execute(null);
        viewModel.ClearAlertQueueCommand.Execute(null);
        viewModel.InstallObsSourcesCommand.Execute(null);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            ["refresh", "suppress", "disable", "enable", "stop", "clear", "install"],
            calls);
    }
}
