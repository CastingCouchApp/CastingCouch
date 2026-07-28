using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class AlertRuntimePageViewModel : ViewModelBase
{
    public AlertRuntimePageViewModel()
    {
        RefreshActionsCommand = new AsyncRelayCommand(
            _ => RefreshActionsRequestedAsync?.Invoke() ??
                 Task.CompletedTask);
        ApplySuppressionCommand = new AsyncRelayCommand(
            _ => ApplySuppressionRequestedAsync?.Invoke() ??
                 Task.CompletedTask);
        DisableStreamerBotAlertsCommand = new AsyncRelayCommand(
            _ => SetStreamerBotAlertsRequestedAsync?.Invoke(false) ??
                 Task.CompletedTask);
        EnableStreamerBotAlertsCommand = new AsyncRelayCommand(
            _ => SetStreamerBotAlertsRequestedAsync?.Invoke(true) ??
                 Task.CompletedTask);
        StopCurrentAlertCommand = new AsyncRelayCommand(
            _ => StopCurrentAlertRequestedAsync?.Invoke() ??
                 Task.CompletedTask);
        ClearAlertQueueCommand = new AsyncRelayCommand(
            _ => ClearAlertQueueRequestedAsync?.Invoke() ??
                 Task.CompletedTask);
        InstallObsSourcesCommand = new AsyncRelayCommand(
            _ => InstallObsSourcesRequestedAsync?.Invoke() ??
                 Task.CompletedTask);
    }

    public IEnumerable<StreamerBotActionOption> Actions
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public bool Enabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool SuppressStreamerBotAlerts
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string DisableActionName
    {
        get;
        set => SetProperty(ref field, value);
    } = "CCS Alerts deaktivieren";

    public string DisableActionId
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string EnableActionName
    {
        get;
        set => SetProperty(ref field, value);
    } = "CCS Alerts aktivieren";

    public string EnableActionId
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string ObsSceneName
    {
        get;
        set => SetProperty(ref field, value);
    } = "_alerts";

    public string ObsMediaSourceName
    {
        get;
        set => SetProperty(ref field, value);
    } = "ccs_alert_media";

    public string ObsTextSourceName
    {
        get;
        set => SetProperty(ref field, value);
    } = "ccs_alert_text";

    public string InterAlertDelayMilliseconds
    {
        get;
        set => SetProperty(ref field, value);
    } = "350";

    public string QueueStatus
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit · Queue: 0";

    public string StreamerBotGroups
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Verfügbare Aktionsgruppen werden nach dem Laden angezeigt.";

    public string StreamerBotStatus
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit.";

    public string InstallStatus
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Die Alert-Szene wird nicht automatisch angelegt. Bitte nach dem Alert-Setup manuell in OBS hinzufügen.";

    public AsyncRelayCommand RefreshActionsCommand { get; }
    public AsyncRelayCommand ApplySuppressionCommand { get; }
    public AsyncRelayCommand DisableStreamerBotAlertsCommand { get; }
    public AsyncRelayCommand EnableStreamerBotAlertsCommand { get; }
    public AsyncRelayCommand StopCurrentAlertCommand { get; }
    public AsyncRelayCommand ClearAlertQueueCommand { get; }
    public AsyncRelayCommand InstallObsSourcesCommand { get; }

    public Func<Task>? RefreshActionsRequestedAsync { get; set; }
    public Func<Task>? ApplySuppressionRequestedAsync { get; set; }
    public Func<bool, Task>? SetStreamerBotAlertsRequestedAsync { get; set; }
    public Func<Task>? StopCurrentAlertRequestedAsync { get; set; }
    public Func<Task>? ClearAlertQueueRequestedAsync { get; set; }
    public Func<Task>? InstallObsSourcesRequestedAsync { get; set; }

    public void Load(
        AlertSettings alerts,
        StreamerBotSettings streamerBot)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(streamerBot);

        Enabled = alerts.Enabled;
        SuppressStreamerBotAlerts =
            streamerBot.SuppressAlertActionsWhenSuiteAlertsEnabled;
        DisableActionName = streamerBot.DisableAlertsActionName;
        DisableActionId = streamerBot.DisableAlertsActionId;
        EnableActionName = streamerBot.EnableAlertsActionName;
        EnableActionId = streamerBot.EnableAlertsActionId;
        ObsSceneName = alerts.ObsSceneName;
        ObsMediaSourceName = alerts.ObsMediaSourceName;
        ObsTextSourceName = alerts.ObsTextSourceName;
        InterAlertDelayMilliseconds =
            alerts.InterAlertDelayMilliseconds.ToString();
    }

    public bool TryApplyTo(
        AlertSettings alerts,
        StreamerBotSettings streamerBot,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(streamerBot);

        if (!int.TryParse(
                InterAlertDelayMilliseconds.Trim(),
                out int delay) ||
            delay < 0)
        {
            error =
                "Die Zwischenpause zwischen Alerts muss eine nichtnegative Zahl sein.";
            return false;
        }

        alerts.Enabled = Enabled;
        alerts.ObsSceneName = Normalize(
            ObsSceneName,
            "_alerts");
        alerts.ObsMediaSourceName = Normalize(
            ObsMediaSourceName,
            "ccs_alert_media");
        alerts.ObsTextSourceName = Normalize(
            ObsTextSourceName,
            "ccs_alert_text");
        alerts.InterAlertDelayMilliseconds = delay;

        streamerBot.SuppressAlertActionsWhenSuiteAlertsEnabled =
            SuppressStreamerBotAlerts;
        streamerBot.DisableAlertsActionName = Normalize(
            DisableActionName,
            "CCS Alerts deaktivieren");
        streamerBot.DisableAlertsActionId = DisableActionId.Trim();
        streamerBot.EnableAlertsActionName = Normalize(
            EnableActionName,
            "CCS Alerts aktivieren");
        streamerBot.EnableAlertsActionId = EnableActionId.Trim();

        error = "";
        Load(alerts, streamerBot);
        return true;
    }

    public void UpdateQueueState(AlertPlaybackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        QueueStatus = state.IsRunning
            ? $"{state.Current?.Type ?? "Alert"} läuft · Queue: {state.QueueLength}"
            : $"Bereit · Queue: {state.QueueLength}";
    }

    public void SetActions(
        IEnumerable<StreamerBotActionOption> actions)
    {
        Actions = actions ?? [];
    }

    public void SelectActions(
        string disableId,
        string disableName,
        string enableId,
        string enableName)
    {
        StreamerBotActionOption? disable = FindAction(
            disableId,
            disableName);
        StreamerBotActionOption? enable = FindAction(
            enableId,
            enableName);
        DisableActionId = disable?.Id ?? disableId;
        DisableActionName = disable?.Name ?? disableName;
        EnableActionId = enable?.Id ?? enableId;
        EnableActionName = enable?.Name ?? enableName;
    }

    public void SetStreamerBotStatus(string status)
        => StreamerBotStatus = status;

    public void SetStreamerBotGroups(
        IEnumerable<string> groups)
    {
        string[] values = [.. groups
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        StreamerBotGroups = values.Length == 0
            ? "Keine Aktionsgruppen gefunden."
            : "Gefundene Aktionsgruppen: " +
              string.Join(", ", values);
    }

    public void SetInstallStatus(string status)
        => InstallStatus = status;

    private StreamerBotActionOption? FindAction(
        string id,
        string name)
        => Actions.FirstOrDefault(action =>
               !string.IsNullOrWhiteSpace(id) &&
               string.Equals(
                   action.Id,
                   id,
                   StringComparison.OrdinalIgnoreCase))
           ?? Actions.FirstOrDefault(action =>
               string.Equals(
                   action.Name,
                   name,
                   StringComparison.OrdinalIgnoreCase));

    private static string Normalize(
        string value,
        string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
}
