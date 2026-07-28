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
    private void PersistDashboardCountdownSettings()
    {
        if (int.TryParse(DashboardCountdownSecondsBox.Text.Trim(), out int seconds))
        {
            _settings.Workflow.StartCountdownSeconds = Math.Max(0, seconds);
        }

        string label = DashboardCountdownLabelBox.Text.Trim();
        _settings.Workflow.CountdownLabel = string.IsNullOrWhiteSpace(label) ? "Countdown" : label;
        DashboardCountdownLabelText.Text = _settings.Workflow.CountdownLabel;
    }

    private void OpenDashboardCountdownSettingsPopup()
    {
        DashboardCountdownSecondsBox.Text = Math.Max(0, _settings.Workflow.StartCountdownSeconds).ToString();
        DashboardCountdownLabelBox.Text = string.IsNullOrWhiteSpace(_settings.Workflow.CountdownLabel)
            ? "Countdown"
            : _settings.Workflow.CountdownLabel;
        DashboardCountdownSettingsPopup.IsOpen = true;
    }

    private void ApplyDashboardCountdownPreset(int seconds)
    {
        DashboardCountdownSecondsBox.Text = Math.Max(0, seconds).ToString();
    }

    private async Task SaveDashboardCountdownSettingsFromPopupAsync()
    {
        PersistDashboardCountdownSettings();
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch
        {
            // Settings-Save ist best-effort.
        }

        DashboardCountdownSettingsPopup.IsOpen = false;
        if (_workflowModule.Service.State.Phase != StreamPhase.Countdown)
        {
            await SyncIdleOverlayCountdownAsync();
            RefreshDashboardCountdownIdleDisplay();
        }
    }

    private async Task StartDashboardOverlayCountdownAsync()
    {
        PersistDashboardCountdownSettings();
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch
        {
            // Settings-Save ist best-effort; Countdown darf trotzdem starten.
        }

        int duration = Math.Max(0, _settings.Workflow.StartCountdownSeconds);
        await ExecuteWorkflowAsync(() => _workflowModule.Service.StartCountdownAsync(duration));
    }

    private async Task ResetDashboardOverlayCountdownAsync()
    {
        if (_workflowModule.Service.State.Phase == StreamPhase.Countdown)
        {
            await ExecuteWorkflowAsync(() => _workflowModule.Service.StopCountdownAsync());
        }

        await SyncIdleOverlayCountdownAsync();
        RefreshDashboardCountdownIdleDisplay();
    }

    private async Task SyncIdleOverlayCountdownAsync()
    {
        int total = Math.Max(0, _settings.Workflow.StartCountdownSeconds);
        string label = string.IsNullOrWhiteSpace(_settings.Workflow.CountdownLabel)
            ? "Countdown"
            : _settings.Workflow.CountdownLabel.Trim();

        try
        {
            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Countdown.IsRunning = false;
                data.Countdown.RemainingSeconds = total;
                data.Countdown.TotalSeconds = total;
                data.Countdown.EndsAt = null;
                data.Countdown.Label = label;
                data.Countdown.Mode = "manual";
            });

            await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppCountdown(
                false,
                total,
                total,
                label,
                null));
        }
        catch
        {
            // Overlay-Sync ist best-effort.
        }
    }

    private void RefreshDashboardCountdownIdleDisplay()
    {
        int total = Math.Max(0, _settings.Workflow.StartCountdownSeconds);
        DashboardCountdownRemainingText.Text = TimeSpan.FromSeconds(total).ToString(@"mm\:ss");
        DashboardCountdownLabelText.Text = string.IsNullOrWhiteSpace(_settings.Workflow.CountdownLabel)
            ? "Countdown"
            : _settings.Workflow.CountdownLabel;
    }
}
