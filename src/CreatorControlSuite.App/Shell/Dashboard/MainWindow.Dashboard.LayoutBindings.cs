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
    private void InitializeDashboardLayoutBindings()
    {
        SettingsPageViewHost.DashboardModuleMoveUpButton.Click += (_, _) => MoveDashboardModuleEditorItem(-1);
        SettingsPageViewHost.DashboardModuleMoveDownButton.Click += (_, _) => MoveDashboardModuleEditorItem(1);
        SettingsPageViewHost.DashboardModuleOrderResetButton.Click += (_, _) =>
        {
            _settings.Dashboard.ModuleOrder = [.. GetDefaultDashboardModuleOrder()];
            LoadDashboardModuleOrderEditor();
            ApplyDashboardModuleOrder();
        };

        SettingsPageViewHost.DashboardApplyLayoutButton.Click += (_, _) =>
        {
            ApplyDashboardCheckboxesToSettings();
            SaveDashboardModuleOrderFromEditor();
            ApplyDashboardModuleOrder();
            ApplyDashboardModuleSizes();
            ApplyDashboardLayout();
        };
        SettingsPageViewHost.DashboardResetLayoutButton.Click += (_, _) =>
        {
            SettingsPageViewHost.DashboardShowServiceStatusBox.IsChecked = true;
            SettingsPageViewHost.DashboardShowStreamControlsBox.IsChecked = true;
            SettingsPageViewHost.DashboardShowLivePanelsBox.IsChecked = true;
            SettingsPageViewHost.DashboardShowQuickServicesBox.IsChecked = true;
            SettingsPageViewHost.DashboardShowWorkflowRailBox.IsChecked = true;
            SettingsPageViewHost.DashboardShowAdvancedToolsBox.IsChecked = true;
            SettingsPageViewHost.DashboardShowNotificationsBox.IsChecked = true;
            SettingsPageViewHost.DashboardShowStreamHistoryBox.IsChecked = true;
            _settings.Dashboard.ModuleOrder = [.. GetDefaultDashboardModuleOrder()];
            LoadDashboardModuleOrderEditor();
            ApplyDashboardCheckboxesToSettings();
            ApplyDashboardModuleOrder();
            ApplyDashboardModuleSizes();
            ApplyDashboardLayout();
        };
    }
}
