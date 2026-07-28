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
    private void RefreshWorkflowUi(WorkflowState state)
    {
        // Workflow- und Twitch-Ereignisse können aus Hintergrundthreads kommen.
        // WPF-Steuerelemente dürfen ausschließlich vom UI-Thread geändert werden.
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => RefreshWorkflowUi(state));
            return;
        }

        WorkflowPageViewHost.ApplyStatus(
            state.Phase.ToString(),
            state.Detail);
        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        _workflowSessionPageViewModel.Update(state, stats);
        if (state.Phase == StreamPhase.Countdown)
        {
            DashboardCountdownRemainingText.Text =
                _workflowSessionPageViewModel.Countdown;
        }
        else
        {
            RefreshDashboardCountdownIdleDisplay();
        }

        // The dashboard must reflect the actual OBS output as well as streams
        // started through the suite workflow. Otherwise a stream started
        // directly in OBS (or through another controller) remains "OFFLINE".
        bool isLive = state.Phase == StreamPhase.Live
            || _lastObsStreamActive
            || _twitchStreamStartedAt.HasValue;
        DateTimeOffset? liveStartedAt = ResolveLiveStreamStartedAt();
        string liveDetail = isLive && liveStartedAt.HasValue
            ? (DateTimeOffset.Now - liveStartedAt.Value).ToString(@"hh\:mm\:ss")
            : state.Detail;

        StreamDashboardStatus.Text = isLive ? "LIVE" : "OFFLINE";
        UpdateStreamLivePulse(isLive);

        StreamDashboardLamp.Fill =
            isLive
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.IndianRed;

        DashboardStreamDetailText.Text = isLive ? liveDetail : "00:00:00";
        RefreshCommunityUi();
        RefreshTwitchProfessionalUi();
        UpdateDashboardSelectedStatistic();
    }
}
