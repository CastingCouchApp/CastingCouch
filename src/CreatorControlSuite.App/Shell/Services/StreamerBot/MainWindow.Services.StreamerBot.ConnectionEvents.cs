#nullable enable

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Logging;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private async Task ReconnectStreamerBotAsync()
    {
        var view = ServicesPageViewHost.StreamerBotServiceViewHost;
        view.ServicesStreamerBotDiagnosticText.Text =
            "Verbindung wird neu aufgebaut …";
        view.ServicesStreamerBotDiagnosticText.Foreground = Brushes.Gold;
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DisconnectStreamerBotAsync();
                await Task.Delay(attempt * 400);
                await ConnectStreamerBotAsync();
                if (!_streamerBotClient.IsConnected)
                {
                    continue;
                }

                await RefreshStreamerBotActionsAsync(showStatus: true);
                view.ServicesStreamerBotDiagnosticText.Text =
                    $"Neu verbunden · Versuch {attempt}/3 · " +
                    "Aktionen aktualisiert.";
                view.ServicesStreamerBotDiagnosticText.Foreground =
                    Brushes.LightGreen;
                return;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }
        }

        view.ServicesStreamerBotDiagnosticText.Text =
            "Neuverbinden fehlgeschlagen: " +
            (lastError?.Message ?? "Keine WebSocket-Verbindung.");
        view.ServicesStreamerBotDiagnosticText.Foreground =
            Brushes.IndianRed;
    }

    private async Task DiagnoseStreamerBotAsync()
    {
        var view = ServicesPageViewHost.StreamerBotServiceViewHost;
        if (!_streamerBotClient.IsConnected)
        {
            view.ServicesStreamerBotDiagnosticText.Text =
                "Nicht verbunden – zuerst die WebSocket-Verbindung " +
                "herstellen.";
            view.ServicesStreamerBotDiagnosticText.Foreground =
                Brushes.IndianRed;
            return;
        }

        try
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            using JsonDocument response =
                await SendStreamerBotRequestAsync(
                    new { request = "GetActions" },
                    TimeSpan.FromSeconds(5));
            TimeSpan elapsed = DateTimeOffset.UtcNow - started;
            int actionCount =
                response.RootElement.TryGetProperty(
                    "actions",
                    out JsonElement actions) &&
                actions.ValueKind == JsonValueKind.Array
                    ? actions.GetArrayLength()
                    : 0;
            bool eventListenerActive =
                _streamerBotEventSocket?.State == WebSocketState.Open;
            view.ServicesStreamerBotDiagnosticText.Text =
                $"WebSocket OK · Antwort " +
                $"{elapsed.TotalMilliseconds:0} ms · " +
                $"{actionCount} Aktionen · Event-Listener " +
                $"{(eventListenerActive ? "aktiv" : "inaktiv")}";
            view.ServicesStreamerBotDiagnosticText.Foreground =
                Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            view.ServicesStreamerBotDiagnosticText.Text =
                "Diagnose fehlgeschlagen: " + exception.Message;
            view.ServicesStreamerBotDiagnosticText.Foreground =
                Brushes.IndianRed;
        }
    }

    private static void SelectStreamerBotAction(
        ComboBox box,
        string id,
        string name)
    {
        if (box.ItemsSource is not
            IEnumerable<StreamerBotActionOption> actions)
        {
            box.Text = name;
            return;
        }

        StreamerBotActionOption? selected = actions.FirstOrDefault(
            action =>
                !string.IsNullOrWhiteSpace(id) &&
                string.Equals(
                    action.Id,
                    id,
                    StringComparison.OrdinalIgnoreCase))
            ?? actions.FirstOrDefault(action =>
                string.Equals(
                    action.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            box.SelectedItem = selected;
        }
        else
        {
            box.Text = name;
        }
    }

    private async Task<JsonDocument> SendStreamerBotRequestAsync(
        object requestBody,
        TimeSpan? timeout = null) =>
        await _streamerBotClient.SendRequestAsync(requestBody, timeout);

    private async Task SetStreamerBotAlertsEnabledAsync(
        bool enabled,
        bool showSuccess = true)
    {
        if (!_streamerBotClient.IsConnected)
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Streamer.bot ist nicht verbunden.");
            return;
        }

        ComboBox settingsBox = enabled
            ? SettingsPageViewHost.SettingsStreamerBotEnableAlertsActionBox
            : SettingsPageViewHost.SettingsStreamerBotDisableAlertsActionBox;
        string selectedId = enabled
            ? _alertRuntimePageViewModel.EnableActionId
            : _alertRuntimePageViewModel.DisableActionId;
        string selectedName = enabled
            ? _alertRuntimePageViewModel.EnableActionName
            : _alertRuntimePageViewModel.DisableActionName;
        StreamerBotActionOption? selected =
            _streamerBotActions.FirstOrDefault(action =>
                string.Equals(
                    action.Id,
                    selectedId,
                    StringComparison.OrdinalIgnoreCase))
            ?? settingsBox.SelectedItem as StreamerBotActionOption;
        string actionName = selected?.Name ??
            GetStreamerBotActionName(
                selectedName,
                settingsBox,
                enabled
                    ? _settings.StreamerBot.EnableAlertsActionName
                    : _settings.StreamerBot.DisableAlertsActionName);
        string actionId = selected?.Id ?? selectedId;
        if (string.IsNullOrWhiteSpace(actionName) &&
            string.IsNullOrWhiteSpace(actionId))
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Bitte zuerst eine vorhandene Streamer.bot-Hilfsaktion " +
                "auswählen.");
            return;
        }

        try
        {
            var action = !string.IsNullOrWhiteSpace(actionId)
                ? new { id = actionId, name = actionName }
                : new { id = "", name = actionName };
            using JsonDocument response =
                await SendStreamerBotRequestAsync(new
                {
                    request = "DoAction",
                    action,
                    args = new
                    {
                        source = "CastingCouch",
                        alertsEnabled = enabled
                    }
                });
            string? status = response.RootElement.TryGetProperty(
                "status",
                out JsonElement statusNode)
                ? statusNode.GetString()
                : null;
            if (!string.Equals(
                    status,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Streamer.bot hat die Aktion nicht bestätigt.");
            }

            if (enabled)
            {
                _settings.StreamerBot.EnableAlertsActionName = actionName;
                _settings.StreamerBot.EnableAlertsActionId = actionId;
            }
            else
            {
                _settings.StreamerBot.DisableAlertsActionName = actionName;
                _settings.StreamerBot.DisableAlertsActionId = actionId;
            }

            _alertRuntimePageViewModel.SelectActions(
                _settings.StreamerBot.DisableAlertsActionId,
                _settings.StreamerBot.DisableAlertsActionName,
                _settings.StreamerBot.EnableAlertsActionId,
                _settings.StreamerBot.EnableAlertsActionName);
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                showSuccess
                    ? $"Streamer.bot hat die Aktion „{actionName}“ " +
                      "bestätigt."
                    : enabled
                        ? "Streamer.bot-Alerts bleiben aktiv."
                        : "Suite-Alerts aktiv: Deaktivierungsaktion wurde " +
                          "von Streamer.bot bestätigt.");
        }
        catch (Exception exception)
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Streamer.bot-Alertsteuerung fehlgeschlagen: " +
                exception.Message);
        }
    }

    private async Task ConnectStreamerBotAsync()
    {
        await DisconnectStreamerBotAsync();
        var view = ServicesPageViewHost.StreamerBotServiceViewHost;
        try
        {
            await _streamerBotClient.ConnectAsync(_settings.StreamerBot);
            await RefreshStreamerBotActionsAsync(showStatus: false);
            await StartStreamerBotEventListenerAsync();
            view.ServicesStreamerBotStatusText.Text =
                _streamerBotClient.Status.Detail;
            view.ServicesStreamerBotDiagnosticText.Text =
                $"WebSocket verbunden · {_streamerBotActions.Count} " +
                "Aktionen geladen · Event-Listener aktiv";
            view.ServicesStreamerBotDiagnosticText.Foreground =
                Brushes.LightGreen;
            view.ServicesStreamerBotStatusText.Foreground =
                Brushes.LightGreen;
            StreamerBotDashboardStatus.Text = "VERBUNDEN";
            StreamerBotDashboardLamp.Fill = Brushes.LimeGreen;
            view.ServicesStreamerBotServicesList.ItemsSource =
                new[]
                {
                    "WebSocket API · verbunden",
                    "OBS · Status über Streamer.bot API verfügbar",
                    "Twitch · Status über Streamer.bot API verfügbar",
                    "YouTube · falls in Streamer.bot eingerichtet"
                };
            await ApplyStreamerBotAlertSuppressionAsync();
        }
        catch (Exception exception)
        {
            view.ServicesStreamerBotStatusText.Text = exception.Message;
            view.ServicesStreamerBotStatusText.Foreground =
                Brushes.IndianRed;
        }
    }

    private async Task StartStreamerBotEventListenerAsync()
    {
        _streamerBotEventCts?.Cancel();
        _streamerBotEventSocket?.Dispose();
        _streamerBotEventCts = new CancellationTokenSource();
        _streamerBotEventSocket = new ClientWebSocket();

        StreamerBotConnectionInfo connection =
            _streamerBotClient.ResolveConnection(_settings.StreamerBot);
        if (!string.IsNullOrWhiteSpace(connection.Password))
        {
            _streamerBotEventSocket.Options.SetRequestHeader(
                "Authorization",
                "Bearer " + connection.Password);
        }

        await _streamerBotEventSocket.ConnectAsync(
            connection.WebSocketUri,
            _streamerBotEventCts.Token);
        string subscribe = JsonSerializer.Serialize(new
        {
            request = "Subscribe",
            id = "ccs-events-" + Guid.NewGuid().ToString("N"),
            events = new
            {
                Twitch = new[]
                {
                    "Follow",
                    "Cheer",
                    "Sub",
                    "ReSub",
                    "GiftSub",
                    "GiftBomb",
                    "Raid"
                },
                General = new[] { "Custom" }
            }
        });
        byte[] bytes = Encoding.UTF8.GetBytes(subscribe);
        await _streamerBotEventSocket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            _streamerBotEventCts.Token);
        _ = Task.Run(() =>
            ListenForStreamerBotAlertEventsAsync(
                _streamerBotEventCts.Token));
    }

    private async Task ListenForStreamerBotAlertEventsAsync(
        CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (!token.IsCancellationRequested &&
                   _streamerBotEventSocket is
                       { State: WebSocketState.Open })
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _streamerBotEventSocket.ReceiveAsync(
                        buffer,
                        token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                using JsonDocument document =
                    JsonDocument.Parse(stream.ToArray());
                StreamerBotEventProjection? projection =
                    StreamerBotApplicationService.TryParseEvent(
                        document.RootElement);
                if (projection is null)
                {
                    continue;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _streamerBotLiveEvents.Insert(
                        0,
                        new StreamerBotLiveEventItem(
                            DateTimeOffset.Now,
                            projection.Source,
                            projection.Type,
                            projection.Summary));
                    while (_streamerBotLiveEvents.Count > 100)
                    {
                        _streamerBotLiveEvents.RemoveAt(
                            _streamerBotLiveEvents.Count - 1);
                    }

                    var view =
                        ServicesPageViewHost.StreamerBotServiceViewHost;
                    view.ServicesStreamerBotLiveEventStatusText.Text =
                        $"Letztes Ereignis: {projection.Type} · " +
                        $"{DateTime.Now:HH:mm:ss}";
                    view.ServicesStreamerBotLiveEventsList.ScrollIntoView(
                        _streamerBotLiveEvents.FirstOrDefault());
                });

                if (!projection.IsKnownAlert)
                {
                    continue;
                }

                string id = Guid.NewGuid().ToString("N");
                _ = PulseExternalAlertAsync(
                    "Streamer.bot",
                    id,
                    TimeSpan.FromSeconds(8));
                await Dispatcher.InvokeAsync(() =>
                {
                    _spotifyAutomationPageViewModel.SetAlertStatus(
                        $"Streamer.bot-Alert erkannt: {projection.Type}",
                        "Warning");
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Streamer.bot",
                "Event-Listener für Alert-Ducking wurde beendet.",
                exception);
        }
    }

    private async Task DisconnectStreamerBotAsync()
    {
        _streamerBotEventCts?.Cancel();
        if (_streamerBotEventSocket is { State: WebSocketState.Open })
        {
            try
            {
                await _streamerBotEventSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "disconnect",
                    CancellationToken.None);
            }
            catch
            {
            }
        }

        _streamerBotEventSocket?.Dispose();
        _streamerBotEventSocket = null;
        _streamerBotEventCts?.Dispose();
        _streamerBotEventCts = null;

        await _streamerBotClient.DisconnectAsync();
        var view = ServicesPageViewHost.StreamerBotServiceViewHost;
        view.ServicesStreamerBotStatusText.Text = "Nicht verbunden";
        view.ServicesStreamerBotStatusText.Foreground =
            Brushes.IndianRed;
        StreamerBotDashboardStatus.Text = "NICHT VERBUNDEN";
        StreamerBotDashboardLamp.Fill = Brushes.IndianRed;
        view.ServicesStreamerBotServicesList.ItemsSource = null;
        view.ServicesStreamerBotActionsList.ItemsSource = null;
        view.ServicesStreamerBotDiagnosticText.Text =
            "Verbindung getrennt.";
        view.ServicesStreamerBotDiagnosticText.Foreground = Brushes.Gray;
        view.ServicesStreamerBotSelectedActionText.Text =
            "Keine Aktion ausgewählt.";
        view.ServicesStreamerBotRunActionButton.IsEnabled = false;
        RefreshDashboardServiceActionButtons();
    }
}
