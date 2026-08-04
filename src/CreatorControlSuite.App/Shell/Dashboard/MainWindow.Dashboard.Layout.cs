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
    private void EnterDashboardFocusMode()
    {
        if (_dashboardFocusModeActive)
        {
            return;
        }

        NormalizeDashboardModuleOrder();
        NormalizeDashboardModuleSizes();

        _dashboardPreFocusOrder =
            [.. _settings.Dashboard.ModuleOrder];
        _dashboardPreFocusSizes =
            new Dictionary<string, string>(
                _settings.Dashboard.ModuleSizes,
                StringComparer.Ordinal);
        _dashboardPreFocusVisibility =
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["ServiceStatus"] = _settings.Dashboard.ShowServiceStatus,
                ["StreamControls"] = _settings.Dashboard.ShowStreamControls,
                ["LivePanels"] = _settings.Dashboard.ShowLivePanels,
                ["QuickServices"] = _settings.Dashboard.ShowQuickServices,
                ["WorkflowRail"] = _settings.Dashboard.ShowWorkflowRail,
                ["AdvancedTools"] = _settings.Dashboard.ShowAdvancedTools,
                ["Notifications"] = _settings.Dashboard.ShowNotifications,
                ["StreamHistory"] = _settings.Dashboard.ShowStreamHistory
            };

        _settings.Dashboard.ModuleOrder =
        [
            "ServiceStatus",
            "StreamControls",
            "LivePanels",
            "AdvancedTools",
            "WorkflowRail",
            "Notifications",
            "QuickServices",
            "StreamHistory"
        ];

        _settings.Dashboard.ShowServiceStatus = true;
        _settings.Dashboard.ShowStreamControls = true;
        _settings.Dashboard.ShowLivePanels = true;
        _settings.Dashboard.ShowAdvancedTools = true;
        _settings.Dashboard.ShowWorkflowRail = true;
        _settings.Dashboard.ShowNotifications = false;
        _settings.Dashboard.ShowQuickServices = false;
        _settings.Dashboard.ShowStreamHistory = false;

        _settings.Dashboard.ModuleSizes["ServiceStatus"] = "Standard";
        _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
        _settings.Dashboard.ModuleSizes["LivePanels"] = "Groß";
        _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Groß";
        _settings.Dashboard.ModuleSizes["WorkflowRail"] = "Standard";

        _dashboardFocusModeActive = true;
        DashboardPageViewHost.DashboardFocusModeButton.Content = "FOKUS BEENDEN";

        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardLayout();

        AddDashboardNotification(
            "Stream-Fokusmodus aktiviert. Das Dashboard zeigt jetzt nur die wichtigsten Live-Bereiche.",
            "Info");
    }

    private void ExitDashboardFocusMode()
    {
        if (!_dashboardFocusModeActive)
        {
            return;
        }

        if (_dashboardPreFocusOrder is not null)
        {
            _settings.Dashboard.ModuleOrder =
                [.. _dashboardPreFocusOrder];
        }

        if (_dashboardPreFocusSizes is not null)
        {
            _settings.Dashboard.ModuleSizes =
                new Dictionary<string, string>(
                    _dashboardPreFocusSizes,
                    StringComparer.Ordinal);
        }

        if (_dashboardPreFocusVisibility is not null)
        {
            _settings.Dashboard.ShowServiceStatus =
                _dashboardPreFocusVisibility["ServiceStatus"];
            _settings.Dashboard.ShowStreamControls =
                _dashboardPreFocusVisibility["StreamControls"];
            _settings.Dashboard.ShowLivePanels =
                _dashboardPreFocusVisibility["LivePanels"];
            _settings.Dashboard.ShowQuickServices =
                _dashboardPreFocusVisibility["QuickServices"];
            _settings.Dashboard.ShowWorkflowRail =
                _dashboardPreFocusVisibility["WorkflowRail"];
            _settings.Dashboard.ShowAdvancedTools =
                _dashboardPreFocusVisibility["AdvancedTools"];
            _settings.Dashboard.ShowNotifications =
                _dashboardPreFocusVisibility["Notifications"];
            _settings.Dashboard.ShowStreamHistory =
                _dashboardPreFocusVisibility["StreamHistory"];
        }

        _dashboardFocusModeActive = false;
        DashboardPageViewHost.DashboardFocusModeButton.Content = "FOKUSMODUS";

        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardLayout();

        AddDashboardNotification(
            "Stream-Fokusmodus beendet. Das vorherige Dashboard-Layout wurde wiederhergestellt.",
            "Info");
    }

    private void ApplySelectedDashboardPreset(
        System.Windows.Controls.ComboBox source)
    {
        if (source.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        string preset = item.Content?.ToString() ?? "Command Center";
        ApplyDashboardPreset(preset);

        SettingsPageViewHost.DashboardPresetBox.SelectedIndex = source.SelectedIndex;
        DashboardPageViewHost.DashboardQuickPresetBox.SelectedIndex = source.SelectedIndex;

        LoadDashboardModuleOrderEditor();
        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardLayout();

        AddDashboardNotification(
            $"Dashboard-Preset „{preset}“ wurde angewendet.",
            "Info");
    }

    private void ApplyDashboardPreset(string preset)
    {
        _settings.Dashboard.ModuleOrder =
            [.. GetDefaultDashboardModuleOrder()];

        _settings.Dashboard.ShowServiceStatus = true;
        _settings.Dashboard.ShowStreamControls = true;
        _settings.Dashboard.ShowLivePanels = true;
        _settings.Dashboard.ShowQuickServices = true;
        _settings.Dashboard.ShowWorkflowRail = true;
        _settings.Dashboard.ShowAdvancedTools = true;
        _settings.Dashboard.ShowNotifications = true;
        _settings.Dashboard.ShowStreamHistory = true;

        NormalizeDashboardModuleSizes();

        switch (preset)
        {
            case "Kompakt":
                foreach (string key in GetDefaultDashboardModuleOrder())
                {
                    _settings.Dashboard.ModuleSizes[key] = "Kompakt";
                }

                _settings.Dashboard.ModuleSizes["LivePanels"] = "Standard";
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Standard";
                break;

            case "Twitch Fokus":
                _settings.Dashboard.ModuleOrder =
                [
                    "ServiceStatus",
                    "StreamControls",
                    "LivePanels",
                    "WorkflowRail",
                    "Notifications",
                    "AdvancedTools",
                    "QuickServices",
                    "StreamHistory"
                ];
                _settings.Dashboard.ModuleSizes["LivePanels"] = "Groß";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
                _settings.Dashboard.ModuleSizes["Notifications"] = "Standard";
                _settings.Dashboard.ShowQuickServices = false;
                break;

            case "OBS Fokus":
                _settings.Dashboard.ModuleOrder =
                [
                    "ServiceStatus",
                    "StreamControls",
                    "AdvancedTools",
                    "WorkflowRail",
                    "LivePanels",
                    "QuickServices",
                    "Notifications",
                    "StreamHistory"
                ];
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Groß";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
                _settings.Dashboard.ShowStreamHistory = false;
                break;

            case "Minimal":
                _settings.Dashboard.ShowLivePanels = false;
                _settings.Dashboard.ShowQuickServices = false;
                _settings.Dashboard.ShowWorkflowRail = false;
                _settings.Dashboard.ShowNotifications = false;
                _settings.Dashboard.ShowStreamHistory = false;
                _settings.Dashboard.ModuleSizes["ServiceStatus"] = "Standard";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Standard";
                break;

            default:
                _settings.Dashboard.ModuleSizes["ServiceStatus"] = "Standard";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Standard";
                _settings.Dashboard.ModuleSizes["LivePanels"] = "Groß";
                _settings.Dashboard.ModuleSizes["QuickServices"] = "Standard";
                _settings.Dashboard.ModuleSizes["WorkflowRail"] = "Groß";
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Groß";
                _settings.Dashboard.ModuleSizes["Notifications"] = "Standard";
                _settings.Dashboard.ModuleSizes["StreamHistory"] = "Groß";
                break;
        }

        SettingsPageViewHost.DashboardShowServiceStatusBox.IsChecked =
            _settings.Dashboard.ShowServiceStatus;
        SettingsPageViewHost.DashboardShowStreamControlsBox.IsChecked =
            _settings.Dashboard.ShowStreamControls;
        SettingsPageViewHost.DashboardShowLivePanelsBox.IsChecked =
            _settings.Dashboard.ShowLivePanels;
        SettingsPageViewHost.DashboardShowQuickServicesBox.IsChecked =
            _settings.Dashboard.ShowQuickServices;
        SettingsPageViewHost.DashboardShowWorkflowRailBox.IsChecked =
            _settings.Dashboard.ShowWorkflowRail;
        SettingsPageViewHost.DashboardShowAdvancedToolsBox.IsChecked =
            _settings.Dashboard.ShowAdvancedTools;
        SettingsPageViewHost.DashboardShowNotificationsBox.IsChecked =
            _settings.Dashboard.ShowNotifications;
        SettingsPageViewHost.DashboardShowStreamHistoryBox.IsChecked =
            _settings.Dashboard.ShowStreamHistory;
    }

    private void NormalizeDashboardModuleSizes()
    {
        _settings.Dashboard.ModuleSizes ??=
            new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string key in GetDefaultDashboardModuleOrder())
        {
            if (!_settings.Dashboard.ModuleSizes.TryGetValue(key, out string? size) ||
                size is not ("Kompakt" or "Standard" or "Groß"))
            {
                _settings.Dashboard.ModuleSizes[key] =
                    key is "LivePanels" or "WorkflowRail" or "AdvancedTools" or "StreamHistory"
                        ? "Groß"
                        : "Standard";
            }
        }
    }

    private void ApplyDashboardModuleSizes()
    {
        NormalizeDashboardModuleSizes();

        foreach (string key in GetDefaultDashboardModuleOrder())
        {
            FrameworkElement? element = GetDashboardModuleElement(key);
            if (element is null)
            {
                continue;
            }

            // Dashboard-Module füllen ihren Grid-Slot; feste Pixelbreiten
            // würden das responsive Layout wieder brechen.
            if (string.Equals(key, "ObsSceneControl", StringComparison.Ordinal))
            {
                // Breite/Höhe folgen der Vorschau, nicht dem Grid-Slot.
                element.Width = double.NaN;
                element.MinWidth = 0;
                element.MaxWidth = double.PositiveInfinity;
                element.HorizontalAlignment = HorizontalAlignment.Left;
                element.VerticalAlignment = VerticalAlignment.Top;
                continue;
            }

            if (string.Equals(key, "TwitchUsers", StringComparison.Ordinal))
            {
                element.Width = double.NaN;
                element.MinWidth = 0;
                element.MaxWidth = double.PositiveInfinity;
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
                element.VerticalAlignment = VerticalAlignment.Stretch;
                continue;
            }

            element.Width = double.NaN;
            element.MinWidth = 0;
            element.MaxWidth = double.PositiveInfinity;
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            element.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private const double DashboardObsPreviewDefaultAspect = 16.0 / 9.0;
    private double _dashboardObsPreviewAspect = DashboardObsPreviewDefaultAspect;

    private static double GetDashboardObsScenePreviewWidth(string size) => size switch
    {
        "Kompakt" => 200,
        "Groß" => 800,
        _ => 400
    };

    private (double Width, double Height) GetDashboardObsScenePreviewSize(string size)
    {
        double width = GetDashboardObsScenePreviewWidth(size);
        double aspect = _dashboardObsPreviewAspect > 0
            ? _dashboardObsPreviewAspect
            : DashboardObsPreviewDefaultAspect;
        double height = Math.Round(width / aspect);
        return (width, height);
    }

    private void ApplyDashboardObsScenePreviewSize()
    {
        string size = _settings.Dashboard.ObsScenePreviewSize;
        if (size is not ("Kompakt" or "Standard" or "Groß"))
        {
            size = "Standard";
            _settings.Dashboard.ObsScenePreviewSize = size;
        }

        (double width, double height) = GetDashboardObsScenePreviewSize(size);
        DashboardPageViewHost.DashboardObsScenePreviewBorder.Width = width;
        DashboardPageViewHost.DashboardObsScenePreviewBorder.MinWidth = width;
        DashboardPageViewHost.DashboardObsScenePreviewBorder.MaxWidth = width;
        DashboardPageViewHost.DashboardObsScenePreviewBorder.Height = height;
        DashboardPageViewHost.DashboardObsScenePreviewBorder.MinHeight = height;
        DashboardPageViewHost.DashboardObsScenePreviewBorder.MaxHeight = height;
        DashboardPageViewHost.DashboardObsSceneControlContent.MaxWidth = width;
        DashboardPageViewHost.DashboardSceneButtonsPanel.MaxWidth = width;

        bool useWidePreviewLayout = string.Equals(size, "Groß", StringComparison.Ordinal);
        MoveDashboardTwitchUsersForLargePreview(useWidePreviewLayout);
        DashboardPageViewHost.DashboardPrimaryRow.RowDefinitions[0].Height =
            new GridLength(1, GridUnitType.Star);
        DashboardPageViewHost.DashboardPrimaryRow.RowDefinitions[1].Height = GridLength.Auto;
        Grid.SetRow(DashboardPageViewHost.DashboardObsSceneColumn, 0);
        Grid.SetColumn(DashboardPageViewHost.DashboardObsSceneColumn, 0);
        Grid.SetColumnSpan(DashboardPageViewHost.DashboardObsSceneColumn, 1);
        Grid.SetRow(DashboardPageViewHost.DashboardPrimaryContentColumn, 0);
        Grid.SetColumn(DashboardPageViewHost.DashboardPrimaryContentColumn, 1);
        Grid.SetColumnSpan(DashboardPageViewHost.DashboardPrimaryContentColumn, 1);
        DashboardPageViewHost.DashboardObsSceneColumn.Margin = new Thickness(0, 0, 8, 0);

        foreach (ComboBoxItem item in DashboardPageViewHost.DashboardObsScenePreviewSizeBox.Items
                     .OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString() ?? item.Content?.ToString(), size, StringComparison.Ordinal))
            {
                DashboardPageViewHost.DashboardObsScenePreviewSizeBox.SelectedItem = item;
                break;
            }
        }
    }

    private void MoveDashboardTwitchUsersForLargePreview(bool useWidePreviewLayout)
    {
        Grid sceneColumn = DashboardPageViewHost.DashboardObsSceneColumn;
        Grid activityGrid = DashboardPageViewHost.DashboardActivityGrid;
        Border usersModule = DashboardPageViewHost.DashboardTwitchUsersModule;
        Border eventsModule = DashboardPageViewHost.DashboardTwitchEventsModule;
        Border chatModule = DashboardPageViewHost.DashboardTwitchChatModule;

        if (useWidePreviewLayout)
        {
            if (ReferenceEquals(usersModule.Parent, sceneColumn))
            {
                sceneColumn.Children.Remove(usersModule);
                activityGrid.Children.Add(usersModule);
            }

            activityGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            activityGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            activityGrid.ColumnDefinitions[0].Width = new GridLength(0.85, GridUnitType.Star);
            activityGrid.ColumnDefinitions[1].Width = new GridLength(1.15, GridUnitType.Star);
            activityGrid.ColumnDefinitions[2].Width = new GridLength(0);
            Grid.SetRow(eventsModule, 0);
            Grid.SetColumn(eventsModule, 0);
            Grid.SetRow(usersModule, 1);
            Grid.SetColumn(usersModule, 0);
            Grid.SetRow(chatModule, 0);
            Grid.SetRowSpan(chatModule, 2);
            Grid.SetColumn(chatModule, 1);
            eventsModule.Margin = new Thickness(0, 0, 4, 4);
            usersModule.Margin = new Thickness(0, 4, 4, 0);
            chatModule.Margin = new Thickness(4, 0, 0, 0);
            return;
        }

        if (ReferenceEquals(usersModule.Parent, activityGrid))
        {
            activityGrid.Children.Remove(usersModule);
            sceneColumn.Children.Add(usersModule);
        }

        activityGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        activityGrid.RowDefinitions[1].Height = new GridLength(0);
        activityGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        activityGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        activityGrid.ColumnDefinitions[2].Width = new GridLength(0);
        Grid.SetRow(usersModule, 1);
        Grid.SetColumn(usersModule, 0);
        Grid.SetRow(eventsModule, 0);
        Grid.SetColumn(eventsModule, 0);
        Grid.SetRow(chatModule, 0);
        Grid.SetRowSpan(chatModule, 1);
        Grid.SetColumn(chatModule, 1);
        eventsModule.Margin = new Thickness(0, 0, 4, 0);
        usersModule.Margin = new Thickness(0, 8, 0, 0);
        chatModule.Margin = new Thickness(4, 0, 0, 0);
    }

    private async Task ApplyDashboardObsScenePreviewSizeFromUiAsync()
    {
        if (!_settingsUiLoaded ||
            DashboardPageViewHost.DashboardObsScenePreviewSizeBox.SelectedItem is not System.Windows.Controls.ComboBoxItem sizeItem)
        {
            return;
        }

        string? size = sizeItem.Tag?.ToString() ?? sizeItem.Content?.ToString();
        if (size is not ("Kompakt" or "Standard" or "Groß"))
        {
            return;
        }

        if (string.Equals(_settings.Dashboard.ObsScenePreviewSize, size, StringComparison.Ordinal))
        {
            return;
        }

        _settings.Dashboard.ObsScenePreviewSize = size;
        ApplyDashboardObsScenePreviewSize();
        await _settingsStore.SaveAsync(_settings);
        await RefreshDashboardObsScenePreviewAsync();
    }

    private void RefreshDashboardModuleSizeEditor()
    {
        if (SettingsPageViewHost.DashboardModuleOrderList.SelectedItem is not string displayName)
        {
            SettingsPageViewHost.DashboardModuleSizeBox.SelectedIndex = -1;
            return;
        }

        string? key = GetDashboardModuleKeyFromDisplayName(displayName);
        if (string.IsNullOrWhiteSpace(key))
        {
            SettingsPageViewHost.DashboardModuleSizeBox.SelectedIndex = -1;
            return;
        }

        NormalizeDashboardModuleSizes();
        string size = _settings.Dashboard.ModuleSizes[key];

        foreach (ComboBoxItem item in SettingsPageViewHost.DashboardModuleSizeBox.Items
                     .OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(
                    item.Content?.ToString(),
                    size,
                    StringComparison.Ordinal))
            {
                SettingsPageViewHost.DashboardModuleSizeBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ApplySelectedDashboardModuleSizeFromSettingsEditor()
    {
        if (SettingsPageViewHost.DashboardModuleOrderList.SelectedItem is not string displayName ||
            SettingsPageViewHost.DashboardModuleSizeBox.SelectedItem is not System.Windows.Controls.ComboBoxItem sizeItem)
        {
            return;
        }

        string? key = GetDashboardModuleKeyFromDisplayName(displayName);
        string? size = sizeItem.Content?.ToString();

        if (string.IsNullOrWhiteSpace(key) ||
            size is not ("Kompakt" or "Standard" or "Groß"))
        {
            return;
        }

        NormalizeDashboardModuleSizes();
        _settings.Dashboard.ModuleSizes[key] = size;
        ApplyDashboardModuleSizes();

        AddDashboardNotification(
            $"{displayName}: Größe auf {size} gesetzt.",
            "Info");
    }

    private void SelectDashboardSectionForSizing(FrameworkElement element)
    {
        _dashboardSelectedSection = element;

        string? key = GetDefaultDashboardModuleOrder()
            .FirstOrDefault(candidate =>
                ReferenceEquals(
                    GetDashboardModuleElement(candidate),
                    element));

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        NormalizeDashboardModuleSizes();
        string size = _settings.Dashboard.ModuleSizes[key];

        foreach (ComboBoxItem item in DashboardPageViewHost.DashboardDirectSizeBox.Items
                     .OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(
                    item.Content?.ToString(),
                    size,
                    StringComparison.Ordinal))
            {
                DashboardPageViewHost.DashboardDirectSizeBox.SelectedItem = item;
                break;
            }
        }

        DashboardPageViewHost.DashboardCommandCenterSummaryText.Text =
            $"Ausgewählt: {GetDashboardModuleDisplayName(key)} · Größe {size}";
    }

    private void ApplySelectedDashboardModuleSizeFromDirectEditor()
    {
        if (!_dashboardLayoutEditMode ||
            _dashboardSelectedSection is null ||
            DashboardPageViewHost.DashboardDirectSizeBox.SelectedItem is not System.Windows.Controls.ComboBoxItem sizeItem)
        {
            return;
        }

        string? key = GetDefaultDashboardModuleOrder()
            .FirstOrDefault(candidate =>
                ReferenceEquals(
                    GetDashboardModuleElement(candidate),
                    _dashboardSelectedSection));

        string? size = sizeItem.Content?.ToString();

        if (string.IsNullOrWhiteSpace(key) ||
            size is not ("Kompakt" or "Standard" or "Groß"))
        {
            return;
        }

        NormalizeDashboardModuleSizes();
        _settings.Dashboard.ModuleSizes[key] = size;
        ApplyDashboardModuleSizes();

        AddDashboardNotification(
            $"{GetDashboardModuleDisplayName(key)}: Größe auf {size} gesetzt.",
            "Info");
    }

    private void ApplyDashboardCheckboxesToSettings()
    {
        _settings.Dashboard.ShowServiceStatus = SettingsPageViewHost.DashboardShowServiceStatusBox.IsChecked == true;
        _settings.Dashboard.ShowStreamControls = SettingsPageViewHost.DashboardShowStreamControlsBox.IsChecked == true;
        _settings.Dashboard.ShowLivePanels = SettingsPageViewHost.DashboardShowLivePanelsBox.IsChecked == true;
        _settings.Dashboard.ShowQuickServices = SettingsPageViewHost.DashboardShowQuickServicesBox.IsChecked == true;
        _settings.Dashboard.ShowWorkflowRail = SettingsPageViewHost.DashboardShowWorkflowRailBox.IsChecked == true;
        _settings.Dashboard.ShowAdvancedTools = SettingsPageViewHost.DashboardShowAdvancedToolsBox.IsChecked == true;
        _settings.Dashboard.ShowNotifications = SettingsPageViewHost.DashboardShowNotificationsBox.IsChecked == true;
        _settings.Dashboard.ShowStreamHistory = SettingsPageViewHost.DashboardShowStreamHistoryBox.IsChecked == true;
    }

    private void ApplyDashboardLayout()
    {
        DashboardServiceStatusSection.Visibility = _settings.Dashboard.ShowServiceStatus
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardStreamControlsSection.Visibility = _settings.Dashboard.ShowStreamControls
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardLivePanelsSection.Visibility = _settings.Dashboard.ShowLivePanels
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardQuickServicesSection.Visibility = _settings.Dashboard.ShowQuickServices
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardWorkflowRailSection.Visibility = _settings.Dashboard.ShowWorkflowRail
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardAdvancedToolsSection.Visibility = _settings.Dashboard.ShowAdvancedTools
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardNotificationsSection.Visibility = _settings.Dashboard.ShowNotifications
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardStreamHistorySection.Visibility = _settings.Dashboard.ShowStreamHistory
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
