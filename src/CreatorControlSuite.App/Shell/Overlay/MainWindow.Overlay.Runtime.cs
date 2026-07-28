#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.App.Helpers;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.Services.CreatorIntelligence;
using CreatorControlSuite.App.Themes;
using CreatorControlSuite.App.Twitch;
using CreatorControlSuite.App.ViewModels;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.App.Views.Dialogs;
using CreatorControlSuite.App.Views.Pages.Music;
using CreatorControlSuite.App.Views.Pages.Workflow;
using CreatorControlSuite.Core.Automation;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Eventing;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Music;
using CreatorControlSuite.Core.Profiles;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Twitch;
using CreatorControlSuite.Core.Updates;
using CreatorControlSuite.Core.Validation;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.Alerts.Models;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Extensions;
using CreatorControlSuite.Modules.Overlay.Models;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Spotify.Models;
using CreatorControlSuite.Modules.StreamDeck;
using CreatorControlSuite.Modules.StreamDeck.Models;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;
using CreatorControlSuite.Modules.Workflow;
using CreatorControlSuite.Modules.Workflow.Models;
using CreatorControlSuite.Modules.YouTubeMusic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using MultiPcDeviceRecord = CreatorControlSuite.Core.Security.PairedAgentDevice;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow : Window
{
    private string ResolveActiveOverlayDataPath()
    {
        string overlayRoot = ResolveConfiguredOverlayRoot();
        if (string.IsNullOrWhiteSpace(overlayRoot))
        {
            overlayRoot = _settings.Overlay.RootPath?.Trim() ?? "";
        }

        if (string.IsNullOrWhiteSpace(overlayRoot))
        {
            throw new InvalidOperationException("Es ist kein Overlay-Ordner ausgewählt.");
        }

        overlayRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(overlayRoot));

        // Bei DenverJohn v18.x ist der Pfad durch die HTML-Struktur eindeutig:
        // Overlay/modules/ui/*.html lädt ../../data/overlay-data.json. Eine alte
        // gespeicherte Root/data-Einstellung darf diesen Pfad nicht überstimmen.
        string denverUi = Path.Combine(overlayRoot, "Overlay", "modules", "ui");
        if (Directory.Exists(denverUi) &&
            (File.Exists(Path.Combine(denverUi, "spotify.html")) ||
             File.Exists(Path.Combine(denverUi, "live-status.html"))))
        {
            return Path.Combine(overlayRoot, "Overlay", "data", "overlay-data.json");
        }

        string? configuredPath = _settings.Overlay.DataFilePath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
        }

        return ResolveOverlayDataPathFromRoot(overlayRoot);
    }

    private static string ResolveOverlayDataPathFromRoot(string overlayRoot)
    {
        string nestedPath = Path.Combine(overlayRoot, "Overlay", "data", "overlay-data.json");
        string rootPath = Path.Combine(overlayRoot, "data", "overlay-data.json");
        return File.Exists(nestedPath) || Directory.Exists(Path.GetDirectoryName(nestedPath)!)
            ? nestedPath
            : rootPath;
    }

    private async Task UpdateActiveOverlayJsonAsync(Action<JsonObject> update)
    {
        await OverlayDataWriteCoordinator.Lock.WaitAsync();
        try
        {
            string targetPath = ResolveActiveOverlayDataPath();
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            JsonObject root;
            if (File.Exists(targetPath))
            {
                try { root = JsonNode.Parse(await File.ReadAllTextAsync(targetPath)) as JsonObject ?? []; }
                catch (JsonException) { root = []; }
            }
            else
            {
                root = [];
            }

            update(root);
            root["updatedAt"] = DateTimeOffset.UtcNow;
            string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            // In-place schreiben: Ein Ersetzen per File.Move trennt Hardlinks und
            // lässt verschiedene OBS-Browserquellen anschließend unterschiedliche
            // Dateiknoten lesen. Die globale Sperre verhindert zugleich verlorene
            // Read-Modify-Write-Updates zwischen Spotify, Live, Twitch und OBS.
            await using var stream = new FileStream(
                targetPath, FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(json);
            await writer.FlushAsync();
            await stream.FlushAsync();
        }
        finally { OverlayDataWriteCoordinator.Lock.Release(); }
    }

    private Task PublishOverlayRealtimeEventAsync(OverlayRealtimeEvent evt) =>
        _overlayRealtimeHub.PublishEventAsync(evt);

    private string? _chatEmoteCatalogBroadcasterId;
    private string? _chatBadgeCatalogBroadcasterId;

    private async Task PublishOverlayChatMessageAsync(TwitchChatMessage message)
    {
        if (!_settings.Overlay.Chat.Enabled)
        {
            return;
        }

        try
        {
            if (IsOverlayChatClearCommand(message))
            {
                await _overlayModule.ChatHistory.ClearAndBroadcastAsync();
                return;
            }

            await EnsureChatEmoteCatalogAsync(message.BroadcasterUserId);
            await EnsureChatBadgeCatalogAsync(message.BroadcasterUserId);

            IReadOnlyDictionary<string, ChatEmoteDefinition> catalog =
                _chatEmoteCatalog.GetActiveMap(_settings.Overlay.Chat);
            IReadOnlyList<OverlayChatPart> enriched = ChatEmoteEnricher.Enrich(
                message.Fragments,
                catalog);
            IReadOnlyList<OverlayChatMessagePart> parts =
            [
                .. enriched.Select(part => new OverlayChatMessagePart(
                    part.Type,
                    part.Text,
                    part.Url,
                    part.Provider))
            ];
            IReadOnlyList<OverlayChatBadgePart> badges =
            [
                .. _chatBadgeCatalog.ResolveBadges(message.Badges)
                    .Select(badge => new OverlayChatBadgePart(
                        badge.SetId,
                        badge.Id,
                        badge.Url,
                        badge.Title))
            ];

            await PublishOverlayRealtimeEventAsync(OverlayEventBridge.FromChatMessage(
                message.MessageId,
                message.ChatterName,
                message.ChatterLogin,
                message.Color,
                badges,
                $"{message.ChatterName}: {message.MessageText}",
                message.ReceivedAt,
                parts,
                message.ChatterUserId));
        }
        catch
        {
            // Chat overlay is best-effort and must not break the in-app chat UI.
        }
    }

    private static bool IsOverlayChatClearCommand(TwitchChatMessage message)
    {
        if (!string.Equals(message.MessageText?.Trim(), "/clear", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return message.Badges.Any(badge =>
            string.Equals(badge.SetId, "broadcaster", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(badge.SetId, "moderator", StringComparison.OrdinalIgnoreCase));
    }

    private Task HandleOverlayChatModerationEventAsync(TwitchEvent twitchEvent)
    {
        if (!_settings.Overlay.Chat.Enabled)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (string.Equals(twitchEvent.Type, "channel.chat.message_delete", StringComparison.Ordinal))
            {
                string messageId = GetTwitchEventData(twitchEvent, "message_id");
                _overlayModule.ChatHistory.RemoveMessage(messageId);
            }
            else if (string.Equals(twitchEvent.Type, "channel.chat.clear", StringComparison.Ordinal))
            {
                _overlayRealtimeHub.ClearBufferedChat();
                _ = _overlayModule.ChatHistory.FlushAsync();
            }
            else if (string.Equals(
                         twitchEvent.Type,
                         "channel.chat.clear_user_messages",
                         StringComparison.Ordinal))
            {
                string userId = GetTwitchEventData(twitchEvent, "target_user_id");
                string userLogin = GetTwitchEventData(twitchEvent, "target_user_login");
                _overlayModule.ChatHistory.RemoveUserMessages(userLogin, userId);
            }
        }
        catch
        {
            // best-effort
        }

        return Task.CompletedTask;
    }

    private static string GetTwitchEventData(TwitchEvent twitchEvent, string key) =>
        twitchEvent.Data.TryGetValue(key, out string? value) ? value ?? "" : "";

    private async Task EnsureChatEmoteCatalogAsync(string broadcasterUserId)
    {
        if (string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            return;
        }

        OverlayChatSettings chat = _settings.Overlay.Chat;
        if (!chat.EnableBttv && !chat.EnableFfz && !chat.EnableSevenTv)
        {
            return;
        }

        if (string.Equals(_chatEmoteCatalogBroadcasterId, broadcasterUserId, StringComparison.Ordinal))
        {
            return;
        }

        await _chatEmoteCatalog.RefreshAsync(broadcasterUserId, chat);
        _chatEmoteCatalogBroadcasterId = broadcasterUserId;
    }

    private async Task EnsureChatBadgeCatalogAsync(string broadcasterUserId)
    {
        if (string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            return;
        }

        if (string.Equals(_chatBadgeCatalogBroadcasterId, broadcasterUserId, StringComparison.Ordinal))
        {
            return;
        }

        await _chatBadgeCatalog.RefreshAsync(_twitchApiClient, broadcasterUserId);
        _chatBadgeCatalogBroadcasterId = broadcasterUserId;
    }

    private async Task RefreshChatEmoteCatalogFromSettingsAsync()
    {
        _chatEmoteCatalogBroadcasterId = null;
        _chatBadgeCatalogBroadcasterId = null;
        string broadcasterId = _twitchModule.GetSnapshot().UserId;
        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            return;
        }

        await EnsureChatEmoteCatalogAsync(broadcasterId);
        await EnsureChatBadgeCatalogAsync(broadcasterId);
    }

    private async Task PublishOverlayWorkflowStateAsync(WorkflowState state)
    {
        string phase = state.Phase.ToString();
        if (!string.Equals(_lastOverlayPublishedPhase, phase, StringComparison.Ordinal))
        {
            _lastOverlayPublishedPhase = phase;
            await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppStreamPhase(phase));
        }

        bool isLive = state.Phase == StreamPhase.Live
            || _lastObsStreamActive
            || _twitchStreamStartedAt.HasValue;
        if (_lastOverlayPublishedLive != isLive)
        {
            _lastOverlayPublishedLive = isLive;
            await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppStreamLive(isLive));
        }

        if (!string.IsNullOrWhiteSpace(state.CurrentScene) &&
            !string.Equals(_lastOverlayPublishedScene, state.CurrentScene, StringComparison.Ordinal))
        {
            _lastOverlayPublishedScene = state.CurrentScene;
            await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppObsScene(state.CurrentScene));
        }

        bool countdownRunning = state.Phase == StreamPhase.Countdown;
        int remaining = Math.Max(0, state.CountdownRemainingSeconds);
        if (_lastOverlayPublishedCountdownRunning != countdownRunning ||
            _lastOverlayPublishedCountdownRemaining != remaining)
        {
            _lastOverlayPublishedCountdownRunning = countdownRunning;
            _lastOverlayPublishedCountdownRemaining = remaining;
            OverlayCountdownState countdown = _overlayModule.Service.Current.Countdown;
            await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppCountdown(
                countdownRunning,
                remaining,
                countdown.TotalSeconds > 0 ? countdown.TotalSeconds : _settings.Workflow.StartCountdownSeconds,
                string.IsNullOrWhiteSpace(countdown.Label) ? _settings.Workflow.CountdownLabel : countdown.Label,
                countdown.EndsAt));
        }
    }
}
