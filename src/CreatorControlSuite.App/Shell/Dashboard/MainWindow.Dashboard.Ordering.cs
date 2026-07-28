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
    private static IReadOnlyList<string> GetDefaultDashboardModuleOrder() =>
    [
        "ConnectionStatus",
        "Community",
        "ObsSceneControl",
        "StreamEnd",
        "StreamControl",
        "QuickServices",
        "SpotifyPlayer",
        "TwitchChat",
        "Workflow",
        "Preflight",
        "Scenes",
        "RaidControl",
        "RaidAssistant",
        "Notifications",
        "TwitchEvents",
        "Automation",
        "LiveEvents",
        "SystemResources",
        "StreamHistory",
        "AudioMixer",
        "TwitchUsers",
        "StreamDeckRemote",
        "AdvancedShortcuts",
        "WorkflowStatus",
    ];

    private static string GetDashboardModuleDisplayName(string key) => key switch
    {
        "ConnectionStatus" => "Verbindungsstatus",
        "StreamControl" => "Streamsteuerung",
        "StreamEnd" => "Streamende & Raid",
        "WorkflowStatus" => "Workflow-Status",
        "ObsSceneControl" => "OBS · Szene",
        "Notifications" => "Notification Center",
        "QuickServices" => "Dienste",
        "RaidControl" => "Raid beim Streamende",
        "Workflow" => "Workflow",
        "Preflight" => "Preflight",
        "Scenes" => "Szenen-Schnellwahl",
        "AudioMixer" => "OBS Audiomixer",
        "RaidAssistant" => "Raid-Assistent",
        "TwitchChat" => "Twitch Chat",
        "TwitchUsers" => "Twitch User",
        "TwitchEvents" => "Letzte Twitch-Events",
        "SpotifyPlayer" => "Spotify Player",
        "Community" => "Community",
        "SystemResources" => "Systemressourcen",
        "StreamDeckRemote" => "Stream Deck & Remote",
        "AdvancedShortcuts" => "Dashboard-Schnellzugriffe",
        "Automation" => "Nächste Automatisierungen",
        "LiveEvents" => "Letzte Events",
        "StreamHistory" => "Stream-Historie",
        _ => key
    };

    private string? GetDashboardModuleKeyFromDisplayName(string displayName)
    {
        return GetDefaultDashboardModuleOrder()
            .FirstOrDefault(key =>
                string.Equals(
                    GetDashboardModuleDisplayName(key),
                    displayName,
                    StringComparison.Ordinal));
    }

    private void NormalizeDashboardModuleOrder()
    {
        IReadOnlyList<string> validKeys = GetDefaultDashboardModuleOrder();
        var normalized = (_settings.Dashboard.ModuleOrder ?? [])
            .Select(key => string.Equals(key, "StreamStatistics", StringComparison.Ordinal)
                ? "Community"
                : key)
            .Where(key => validKeys.Contains(key, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (string key in validKeys)
        {
            if (!normalized.Contains(key, StringComparer.Ordinal))
            {
                normalized.Add(key);
            }
        }

        _settings.Dashboard.ModuleOrder = normalized;
        _settings.Dashboard.HiddenModules ??= [];
        if (_settings.Dashboard.HiddenModules.RemoveAll(key =>
                string.Equals(key, "StreamStatistics", StringComparison.Ordinal)) > 0
            && !_settings.Dashboard.HiddenModules.Contains("Community", StringComparer.Ordinal))
        {
            _settings.Dashboard.HiddenModules.Add("Community");
        }

        MigrateDashboardModuleSettingKey(
            _settings.Dashboard.ModuleZones,
            "StreamStatistics",
            "Community");
        MigrateDashboardModuleSettingKey(
            _settings.Dashboard.ModuleSizes,
            "StreamStatistics",
            "Community");
        MigrateDashboardModuleSettingKey(
            _settings.Dashboard.ModuleWidths,
            "StreamStatistics",
            "Community");
        MigrateDashboardModuleSettingKey(
            _settings.Dashboard.ModuleHeights,
            "StreamStatistics",
            "Community");

        _settings.Dashboard.ModuleWidths ??=
            new Dictionary<string, double>(StringComparer.Ordinal);
        _settings.Dashboard.ModuleHeights ??=
            new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (string key in validKeys)
        {
            if (!_settings.Dashboard.ModuleWidths.ContainsKey(key))
            {
                _settings.Dashboard.ModuleWidths[key] = 320;
            }

            if (!_settings.Dashboard.ModuleHeights.ContainsKey(key))
            {
                _settings.Dashboard.ModuleHeights[key] = 180;
            }
        }
    }

    private static void MigrateDashboardModuleSettingKey<T>(
        Dictionary<string, T>? map,
        string fromKey,
        string toKey)
    {
        if (map is null || !map.Remove(fromKey, out T? value))
        {
            return;
        }

        map.TryAdd(toKey, value);
    }

    private void LoadDashboardModuleOrderEditor()
    {
        NormalizeDashboardModuleOrder();
        _dashboardModuleOrderItems.Clear();

        foreach (string key in _settings.Dashboard.ModuleOrder)
        {
            _dashboardModuleOrderItems.Add(GetDashboardModuleDisplayName(key));
        }
    }

    private void SaveDashboardModuleOrderFromEditor()
    {
        var keys = _dashboardModuleOrderItems
            .Select(GetDashboardModuleKeyFromDisplayName)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToList();

        _settings.Dashboard.ModuleOrder = keys;
        NormalizeDashboardModuleOrder();
    }

    private string? GetDashboardModuleKey(FrameworkElement element)
    {
        foreach (string key in GetDefaultDashboardModuleOrder())
        {
            if (ReferenceEquals(GetDashboardModuleElement(key), element))
            {
                return key;
            }
        }

        return null;
    }

    private FrameworkElement? GetDashboardModuleElement(string key) => key switch
    {
        "ConnectionStatus" => DashboardServiceStatusSection,
        "StreamControl" => DashboardPageViewHost.DashboardStreamControlModule,
        "StreamEnd" => DashboardPageViewHost.DashboardStreamEndModule,
        "WorkflowStatus" => DashboardPageViewHost.DashboardWorkflowStatusModule,
        "ObsSceneControl" => DashboardPageViewHost.DashboardObsSceneControlModule,
        "Notifications" => DashboardPageViewHost.DashboardNotificationCenterModule,
        "QuickServices" => DashboardPageViewHost.DashboardQuickServicesSection,
        "RaidControl" => DashboardPageViewHost.DashboardRaidControlModule,
        "Workflow" => DashboardPageViewHost.DashboardWorkflowRailSection,
        "Preflight" => DashboardPageViewHost.DashboardPreflightModule,
        "Scenes" => DashboardPageViewHost.DashboardScenesModule,
        "AudioMixer" => DashboardPageViewHost.DashboardAudioMixerModule,
        "RaidAssistant" => DashboardPageViewHost.DashboardRaidAssistantModule,
        "TwitchChat" => DashboardPageViewHost.DashboardTwitchChatModule,
        "TwitchUsers" => DashboardPageViewHost.DashboardTwitchUsersModule,
        "TwitchEvents" => DashboardPageViewHost.DashboardTwitchEventsModule,
        "SpotifyPlayer" => DashboardPageViewHost.DashboardSpotifyPlayerModule,
        "Community" => DashboardCommunityModule,
        "SystemResources" => DashboardPageViewHost.DashboardSystemResourcesModule,
        "StreamDeckRemote" => DashboardPageViewHost.DashboardStreamDeckRemoteModule,
        "AdvancedShortcuts" => DashboardPageViewHost.DashboardAdvancedShortcutsModule,
        "Automation" => DashboardPageViewHost.DashboardAutomationModule,
        "LiveEvents" => DashboardPageViewHost.DashboardLiveEventsModule,
        "StreamHistory" => DashboardPageViewHost.DashboardStreamHistorySection,
        _ => null
    };

    private void ApplyDashboardModuleOrder()
    {
        // Responsive Dashboard: Module bleiben in ihren XAML-Grid-Slots.
        // Reihenfolge wird nicht mehr zur Laufzeit umgehängt.
    }

    private void RemoveDashboardElementFromCurrentParent(
        FrameworkElement element)
    {
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
        }
    }

    private void MoveDashboardModuleEditorItem(int direction)
    {
        int index = SettingsPageViewHost.DashboardModuleOrderList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        int targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= _dashboardModuleOrderItems.Count)
        {
            return;
        }

        string item = _dashboardModuleOrderItems[index];
        _dashboardModuleOrderItems.RemoveAt(index);
        _dashboardModuleOrderItems.Insert(targetIndex, item);
        SettingsPageViewHost.DashboardModuleOrderList.SelectedIndex = targetIndex;

        SaveDashboardModuleOrderFromEditor();
        ApplyDashboardModuleOrder();
    }

    private void DashboardModuleOrderList_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        _dashboardModuleDragStart = e.GetPosition(SettingsPageViewHost.DashboardModuleOrderList);
        _dashboardDraggedModuleName =
            FindListBoxItemTextFromPoint(SettingsPageViewHost.DashboardModuleOrderList, _dashboardModuleDragStart);
    }

    private void DashboardModuleOrderList_PreviewMouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed ||
            string.IsNullOrWhiteSpace(_dashboardDraggedModuleName))
        {
            return;
        }

        Point current = e.GetPosition(SettingsPageViewHost.DashboardModuleOrderList);
        if (Math.Abs(current.X - _dashboardModuleDragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dashboardModuleDragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(
            SettingsPageViewHost.DashboardModuleOrderList,
            _dashboardDraggedModuleName,
            DragDropEffects.Move);
    }

    private void DashboardModuleOrderList_Drop(
        object sender,
        DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            return;
        }

        string? dragged = e.Data.GetData(DataFormats.StringFormat) as string;
        if (string.IsNullOrWhiteSpace(dragged))
        {
            return;
        }

        string? target =
            FindListBoxItemTextFromPoint(
                SettingsPageViewHost.DashboardModuleOrderList,
                e.GetPosition(SettingsPageViewHost.DashboardModuleOrderList));

        int oldIndex = _dashboardModuleOrderItems.IndexOf(dragged);
        int targetIndex = string.IsNullOrWhiteSpace(target)
            ? _dashboardModuleOrderItems.Count - 1
            : _dashboardModuleOrderItems.IndexOf(target);

        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return;
        }

        _dashboardModuleOrderItems.RemoveAt(oldIndex);
        if (oldIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, _dashboardModuleOrderItems.Count);
        _dashboardModuleOrderItems.Insert(targetIndex, dragged);
        SettingsPageViewHost.DashboardModuleOrderList.SelectedItem = dragged;

        SaveDashboardModuleOrderFromEditor();
        ApplyDashboardModuleOrder();
    }

    private static string? FindListBoxItemTextFromPoint(
        System.Windows.Controls.ListBox listBox,
        Point point)
    {
        var element = listBox.InputHitTest(point) as DependencyObject;

        while (element is not null &&
               element is not System.Windows.Controls.ListBoxItem)
        {
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return (element as System.Windows.Controls.ListBoxItem)?.Content?.ToString();
    }

    private static T? FindVisualParent<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void RegisterDashboardDirectDragHandlers()
    {
        // Fixed reference dashboard: module dragging is disabled.
    }

    private ContextMenu BuildDashboardModuleContextMenu(string key)
    {
        var menu = new ContextMenu();

        var hideItem = new MenuItem
        {
            Header = "Modul ausblenden"
        };
        hideItem.Click += (_, _) =>
            SetDashboardModuleHidden(key, true);
        menu.Items.Add(hideItem);

        return menu;
    }

    private void SetDashboardModuleHidden(string key, bool hidden)
    {
        _settings.Dashboard.HiddenModules ??= [];

        _settings.Dashboard.HiddenModules.RemoveAll(
            item => string.Equals(item, key, StringComparison.Ordinal));

        if (hidden)
        {
            _settings.Dashboard.HiddenModules.Add(key);
        }

        ApplyDashboardModuleOrder();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void RestoreAllDashboardModules()
    {
        _settings.Dashboard.HiddenModules = [];
        ApplyDashboardModuleOrder();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void ToggleDashboardLayoutEditMode()
    {
        _dashboardLayoutEditMode = false;
        AddDashboardNotification(
            "Das Dashboard verwendet die feste Referenz-Anordnung.",
            "Info");
    }

    private void DashboardSection_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dashboardLayoutEditMode || sender is not FrameworkElement element)
        {
            return;
        }

        _dashboardDraggedSection = element;
        _dashboardDirectDragStart = e.GetPosition(DashboardPageViewHost.DashboardContentStack);
        SelectDashboardSectionForSizing(element);

        element.CaptureMouse();
        e.Handled = true;
    }

    private void DashboardSection_PreviewMouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (!_dashboardLayoutEditMode ||
            e.LeftButton != System.Windows.Input.MouseButtonState.Pressed ||
            _dashboardDraggedSection is null)
        {
            return;
        }

        Point current = e.GetPosition(DashboardPageViewHost.DashboardContentStack);

        if (Math.Abs(current.X - _dashboardDirectDragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dashboardDirectDragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        MoveDashboardSectionToPointer(_dashboardDraggedSection, current);
        _dashboardDirectDragStart = current;
        e.Handled = true;
    }

    private void MoveDashboardSectionToPointer(
        FrameworkElement dragged,
        Point pointer)
    {
        if (!DashboardPageViewHost.DashboardContentStack.Children.Contains(dragged))
        {
            return;
        }

        int currentIndex =
            DashboardPageViewHost.DashboardContentStack.Children.IndexOf(dragged);

        Point pointerPosition =
            System.Windows.Input.Mouse.GetPosition(
                DashboardPageViewHost.DashboardContentStack);

        int targetIndex = currentIndex;
        double bestDistance = double.MaxValue;

        for (int index = 0;
             index < DashboardPageViewHost.DashboardContentStack.Children.Count;
             index++)
        {
            if (DashboardPageViewHost.DashboardContentStack.Children[index]
                    is not FrameworkElement candidate ||
                ReferenceEquals(candidate, dragged) ||
                candidate.Visibility != Visibility.Visible)
            {
                continue;
            }

            Point topLeft =
                candidate.TranslatePoint(
                    new Point(0, 0),
                    DashboardPageViewHost.DashboardContentStack);

            double centerX =
                topLeft.X + (candidate.ActualWidth / 2);
            double centerY =
                topLeft.Y + (candidate.ActualHeight / 2);

            double deltaX = pointerPosition.X - centerX;
            double deltaY = pointerPosition.Y - centerY;
            double distance =
                (deltaX * deltaX) + (deltaY * deltaY);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                targetIndex = index;
            }
        }

        if (targetIndex == currentIndex)
        {
            return;
        }

        DashboardPageViewHost.DashboardContentStack.Children.RemoveAt(currentIndex);

        if (currentIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(
            targetIndex,
            0,
            DashboardPageViewHost.DashboardContentStack.Children.Count);

        DashboardPageViewHost.DashboardContentStack.Children.Insert(
            targetIndex,
            dragged);
        SyncDashboardContentStackRows();
    }

    private void SyncDashboardContentStackRows()
    {
        for (int index = 0; index < DashboardPageViewHost.DashboardContentStack.Children.Count; index++)
        {
            if (DashboardPageViewHost.DashboardContentStack.Children[index] is UIElement child)
            {
                Grid.SetRow(child, index);
            }
        }
    }

    private void DashboardSection_PreviewMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        FinishDashboardDirectDrag(sender as FrameworkElement);
        e.Handled = true;
    }

    private void DashboardSection_LostMouseCapture(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (_dashboardDraggedSection is not null)
        {
            FinishDashboardDirectDrag(sender as FrameworkElement);
        }
    }

    private void FinishDashboardDirectDrag(FrameworkElement? element)
    {
        if (_dashboardDraggedSection is null)
        {
            return;
        }

        _dashboardDraggedSection = null;

        if (element?.IsMouseCaptured == true)
        {
            element.ReleaseMouseCapture();
        }

        SaveDashboardModuleOrderFromVisualTree();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void DashboardContentStack_DragOver(
        object sender,
        DragEventArgs e)
    {
        if (!_dashboardLayoutEditMode ||
            !e.Data.GetDataPresent(typeof(FrameworkElement)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void DashboardContentStack_Drop(
        object sender,
        DragEventArgs e)
    {
        if (!_dashboardLayoutEditMode)
        {
            return;
        }


        if (e.Data.GetData(typeof(FrameworkElement)) is not FrameworkElement dragged ||
            !DashboardPageViewHost.DashboardContentStack.Children.Contains(dragged))
        {
            return;
        }

        MoveDashboardSectionToPointer(
            dragged,
            e.GetPosition(DashboardPageViewHost.DashboardContentStack));

        SaveDashboardModuleOrderFromVisualTree();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
        e.Handled = true;
    }

    private void SaveDashboardModuleOrderFromVisualTree()
    {
        var order = new List<string>();

        foreach (object? child in DashboardPageViewHost.DashboardContentStack.Children)
        {
            if (child is not FrameworkElement element)
            {
                continue;
            }

            string? key = GetDashboardModuleKey(element);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            order.Add(key);
        }

        foreach (string key in GetDefaultDashboardModuleOrder())
        {
            if (!order.Contains(key, StringComparer.Ordinal))
            {
                order.Add(key);
            }
        }

        _settings.Dashboard.ModuleOrder = order;
    }
}
