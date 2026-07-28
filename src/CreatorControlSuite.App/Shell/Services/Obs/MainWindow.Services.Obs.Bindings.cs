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
    private void InitializeObsBindings()
    {
        SettingsPageViewHost.ConnectObsButton.Click += async (_, _) => await ConnectObsAsync();
        SettingsPageViewHost.DisconnectObsButton.Click += async (_, _) => await DisconnectObsAsync();
        SettingsPageViewHost.RefreshObsButton.Click += async (_, _) => await RefreshObsAsync();
        SettingsPageViewHost.RefreshWorkflowScenesButton.Click += async (_, _) => await RefreshObsAsync();
        SettingsPageViewHost.StartSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        SettingsPageViewHost.LiveSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        SettingsPageViewHost.PauseSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        SettingsPageViewHost.EndSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        SettingsPageViewHost.SwitchObsSceneButton.Click += async (_, _) => await SwitchObsSceneAsync();
        SettingsPageViewHost.StartObsStreamButton.Click += async (_, _) => await StartObsStreamAsync();
        SettingsPageViewHost.StopObsStreamButton.Click += async (_, _) => await StopObsStreamAsync();

        _obsClient.ConnectionStateChanged += (_, connected) =>
        {
            Dispatcher.Invoke(() =>
            {
                ObsDashboardStatus.Text = connected ? "VERBUNDEN" : "NICHT VERBUNDEN";
                ObsDashboardLamp.Fill = connected
                    ? System.Windows.Media.Brushes.LimeGreen
                    : System.Windows.Media.Brushes.IndianRed;
                SettingsPageViewHost.ObsConnectionStatusText.Text = connected
                    ? "Verbunden"
                    : "Nicht verbunden";
                SettingsPageViewHost.ObsConnectionStatusText.Foreground = connected
                    ? System.Windows.Media.Brushes.LightGreen
                    : System.Windows.Media.Brushes.Gray;
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsStatusText.Text = SettingsPageViewHost.ObsConnectionStatusText.Text;
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsStatusText.Foreground = SettingsPageViewHost.ObsConnectionStatusText.Foreground;
                RefreshDashboardServiceActionButtons();
            });
        };

        _obsClient.SceneCollectionChanged += (_, _) => _ = Dispatcher.InvokeAsync(RefreshObsAsync);
        _obsClient.SceneItemsChanged += (_, _) => _ = Dispatcher.InvokeAsync(RefreshServicesObsSceneItemsAsync);
        _obsClient.InputsChanged += (_, _) => _ = Dispatcher.InvokeAsync(async () =>
        {
            await RefreshObsAsync();
            await RefreshSelectedObsInputStateAsync();
        });
        _obsClient.InputVolumeMeters += (_, meters) => _ = Dispatcher.InvokeAsync(() => UpdateObsLiveMeters(meters));

        _obsClient.CurrentProgramSceneChanged += (_, sceneName) =>
        {
            Dispatcher.Invoke(() =>
            {
                SettingsPageViewHost.ObsConnectionStatusText.Text =
                    "Verbunden · Szene: " + sceneName;
                DashboardPageViewHost.DashboardCurrentSceneText.Text = sceneName;
                _servicesObsCurrentScene = sceneName;
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsCurrentSceneText.Text = "Aktuelle Szene: " + sceneName;
                _automationCurrentScene = sceneName;
                _automationSceneActivatedAt = DateTimeOffset.UtcNow;
                HighlightDashboardSceneButtons(sceneName);
                foreach (TimedAutomationRuleSettings? sceneRule in _settings.Workflow.TimedAutomations
                             .Where(rule => string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase)
                                            && !rule.OncePerStream
                                            && string.Equals(rule.TriggerScene, sceneName, StringComparison.OrdinalIgnoreCase)))
                {
                    _executedTimedAutomationRuleIds.Remove(sceneRule.Id);
                }
                CancelPendingSceneAutomationExecutions();
            });
            _ = _creatorIntelligence.RecordAsync("obs.scene.changed", new { scene = sceneName, viewers = _currentLiveViewerCount });
            _ = PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppObsScene(sceneName));
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await ExecuteSpotifySceneAutomationAsync(sceneName);
                await RefreshDashboardObsScenePreviewAsync(sceneName);
            });
        };
    }
}
