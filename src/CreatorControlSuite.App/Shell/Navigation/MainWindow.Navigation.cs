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
    public void OpenSettingsPage()
    {
        ShowPage(SettingsPage);
    }

    private void ShowServicesOverview()
    {
        ShowPage(ServicesPage);
        ServicesNavigationPanel.Visibility = Visibility.Visible;
        ServicesPageViewHost.ShowOverview();
        SetActiveNavigationButton(ServicesButton);
    }

    private void NavigateToServicesTab(
        int tabIndex,
        Button? navigationButton = null)
    {
        ShowPage(ServicesPage);
        ServicesPageViewHost.SelectService(tabIndex);

        ServicesNavigationPanel.Visibility = Visibility.Visible;
        SetActiveNavigationButton(
            navigationButton ?? ServicesButton);
    }

    private void NavigateToSettingsTab(
        int tabIndex,
        Button? navigationButton = null)
    {
        ShowPage(SettingsPage);

        SettingsPageViewHost.SelectTab(tabIndex);

        ServicesNavigationPanel.Visibility =
            ReferenceEquals(navigationButton, ServicesStreamDeckButton)
                ? Visibility.Visible
                : Visibility.Collapsed;

        SetActiveNavigationButton(
            navigationButton ?? SettingsButton);
    }

    private void SetActiveNavigationButton(Button? activeButton)
    {
        var navigationButtons = new Button[]
        {
            DashboardButton,
            PlayerButton,
            ServicesButton,
            ServicesSpotifyButton,
            ServicesTwitchButton,
            ServicesObsButton,
            ServicesStreamerBotButton,
            ServicesStreamDeckButton,
            WorkflowButton,
            StatisticsButton,
            OverlaysButton,
            AlertsButton,
            SettingsButton,
            DiagnosticsButton
        };

        foreach (Button button in navigationButtons)
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            button.FontWeight = FontWeights.Normal;
        }

        if (activeButton is null)
        {
            return;
        }

        activeButton.Background =
            _themeService.GetBrush("NavActiveBackgroundBrush")
            ?? new SolidColorBrush(Color.FromRgb(42, 23, 10));
        activeButton.Foreground =
            _themeService.GetBrush("NavActiveForegroundBrush")
            ?? new SolidColorBrush(Color.FromRgb(255, 122, 26));
        activeButton.FontWeight = FontWeights.SemiBold;
    }

    private void ShowPage(UIElement page)
    {
        var pages = new UIElement[]
        {
            DashboardPage,
            MusicPlayerPage,
            ServicesPage,
            WorkflowPage,
            OverlayPage,
            AlertsPage,
            SettingsPage,
            DiagnosticsPage,
            StatisticsPage,
            ProfilesPage,
            MultiPcPage,
            AboutPage
        };

        foreach (UIElement candidate in pages)
        {
            candidate.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(candidate, 0);
        }

        page.Visibility = Visibility.Visible;
        Panel.SetZIndex(page, 1);

        string pageKey =
            ReferenceEquals(page, DashboardPage) ? "dashboard" :
            ReferenceEquals(page, MusicPlayerPage) ? "music" :
            ReferenceEquals(page, ServicesPage) ? "services" :
            ReferenceEquals(page, WorkflowPage) ? "workflow" :
            ReferenceEquals(page, OverlayPage) ? "overlay" :
            ReferenceEquals(page, AlertsPage) ? "alerts" :
            ReferenceEquals(page, SettingsPage) ? "settings" :
            ReferenceEquals(page, DiagnosticsPage) ? "diagnostics" :
            ReferenceEquals(page, StatisticsPage) ? "statistics" :
            ReferenceEquals(page, ProfilesPage) ? "profiles" :
            ReferenceEquals(page, MultiPcPage) ? "multipc" :
            ReferenceEquals(page, AboutPage) ? "about" :
            "unknown";
        _navigationService.Navigate(pageKey);
        _eventBus.Publish(new NavigationRequested(pageKey));

        if (ReferenceEquals(page, DashboardPage))
        {
            SetActiveNavigationButton(DashboardButton);
        }
        else if (ReferenceEquals(page, MusicPlayerPage))
        {
            SetActiveNavigationButton(PlayerButton);
        }
        else if (ReferenceEquals(page, ServicesPage))
        {
            SetActiveNavigationButton(ServicesButton);
        }
        else if (ReferenceEquals(page, WorkflowPage))
        {
            SetActiveNavigationButton(WorkflowButton);
        }
        else if (ReferenceEquals(page, StatisticsPage))
        {
            SetActiveNavigationButton(StatisticsButton);
        }
        else if (ReferenceEquals(page, MultiPcPage))
        {
            SetActiveNavigationButton(MultiPcButton);
        }
        else if (ReferenceEquals(page, OverlayPage))
        {
            SetActiveNavigationButton(OverlaysButton);
        }
        else if (ReferenceEquals(page, AlertsPage))
        {
            SetActiveNavigationButton(AlertsButton);
        }
        else if (ReferenceEquals(page, SettingsPage))
        {
            SetActiveNavigationButton(SettingsButton);
        }
        else if (ReferenceEquals(page, DiagnosticsPage))
        {
            SetActiveNavigationButton(DiagnosticsButton);
        }
        else
        {
            SetActiveNavigationButton(null);
        }
    }
}
