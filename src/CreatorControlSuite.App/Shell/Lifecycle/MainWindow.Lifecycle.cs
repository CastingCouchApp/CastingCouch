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
    private void TitleBarMinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void TitleBarMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowMainWindowClose)
        {
            return;
        }

        if (_streamEndFlowActive)
        {
            e.Cancel = true;
            _activeStreamEndDialog?.Activate();
            MessageBox.Show(
                this,
                "Das Streamende läuft noch. Bitte warte, bis der Stream beendet ist, oder brich den Ablauf ab.",
                "CastingCouch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!_lastObsStreamActive)
        {
            return;
        }

        e.Cancel = true;
        MessageBoxResult result = MessageBox.Show(
            this,
            "Der Stream läuft noch. Die Anwendung kann erst geschlossen werden, wenn der Stream beendet ist.\n\nStreamende-Dialog öffnen?",
            "CastingCouch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _closeAfterStreamEnd = true;
        _ = StopObsStreamAsync();
    }

    private void TryCloseApplicationAfterStreamEnd()
    {
        if (!_closeAfterStreamEnd)
        {
            return;
        }

        _closeAfterStreamEnd = false;
        _allowMainWindowClose = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void UpdateTitleBarMaximizeButton()
    {
        if (TitleBarMaximizeButton is null || TitleBarMaximizeIcon is null || TitleBarRestoreIcon is null)
        {
            return;
        }

        bool maximized = WindowState == WindowState.Maximized;
        TitleBarMaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        TitleBarRestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        TitleBarMaximizeButton.ToolTip = maximized ? "Wiederherstellen" : "Maximieren";
    }

    private void GeneralSettingsPageViewModelOnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeneralSettingsPageViewModel.TitleBarWidgetCardsEnabled))
        {
            ApplyTitleBarChrome();
        }
    }

    private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        ContextMenu menu = BuildTitleBarEditContextMenu();
        if (sender is FrameworkElement target)
        {
            menu.PlacementTarget = target;
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu BuildTitleBarEditContextMenu()
    {
        _settings.General.TitleBarHiddenWidgets ??= [];
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = "TitleBar-Widgets",
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        });
        menu.Items.Add(new Separator());

        foreach ((string key, string label) in TitleBarWidgetVisibility.All)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = TitleBarWidgetVisibility.IsVisible(
                    _settings.General.TitleBarHiddenWidgets,
                    key),
                StaysOpenOnClick = true,
                Tag = key
            };
            item.Click += TitleBarWidgetVisibilityMenuItem_Click;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var cardsItem = new MenuItem
        {
            Header = "Als Cards darstellen",
            IsCheckable = true,
            IsChecked = _generalSettingsPageViewModel.TitleBarWidgetCardsEnabled,
            StaysOpenOnClick = true
        };
        cardsItem.Click += (_, _) =>
        {
            bool enabled = cardsItem.IsChecked == true;
            _generalSettingsPageViewModel.TitleBarWidgetCardsEnabled = enabled;
            _settings.General.TitleBarWidgetCardsEnabled = enabled;
            _ = _settingsStore.SaveAsync(_settings);
        };
        menu.Items.Add(cardsItem);

        var showAllItem = new MenuItem { Header = "Alle Widgets einblenden" };
        showAllItem.Click += (_, _) =>
        {
            _settings.General.TitleBarHiddenWidgets = [];
            ApplyTitleBarChrome();
            _ = _settingsStore.SaveAsync(_settings);
        };
        menu.Items.Add(showAllItem);
        return menu;
    }

    private void TitleBarWidgetVisibilityMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string key } item)
        {
            return;
        }

        _settings.General.TitleBarHiddenWidgets ??= [];
        TitleBarWidgetVisibility.SetHidden(
            _settings.General.TitleBarHiddenWidgets,
            key,
            hide: item.IsChecked != true);
        ApplyTitleBarChrome();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void ApplyTitleBarChrome()
    {
        ApplyTitleBarWidgetChrome(_generalSettingsPageViewModel.TitleBarWidgetCardsEnabled);
        ApplyTitleBarWidgetVisibility();
    }

    private void ApplyTitleBarWidgetChrome(bool cardsEnabled)
    {
        string styleKey = cardsEnabled
            ? "TitleBarWidgetCardStyle"
            : "TitleBarWidgetStyle";
        if (TryFindResource(styleKey) is not Style widgetStyle)
        {
            return;
        }

        foreach (Border border in EnumerateLogicalBorders(DashboardTopStatusRow))
        {
            if (Equals(border.Tag, "TitleBarChromeWidget"))
            {
                border.Style = widgetStyle;
            }
        }
    }

    private void ApplyTitleBarWidgetVisibility()
    {
        _settings.General.TitleBarHiddenWidgets ??= [];
        IReadOnlyList<string> hidden = _settings.General.TitleBarHiddenWidgets;
        bool cardsEnabled = _generalSettingsPageViewModel.TitleBarWidgetCardsEnabled;

        SetTitleBarWidgetVisibility(DashboardTitleBarStreamWidget, TitleBarWidgetVisibility.Stream, hidden);
        SetTitleBarWidgetVisibility(DashboardStreamQualityModule, TitleBarWidgetVisibility.Quality, hidden);
        SetTitleBarWidgetVisibility(DashboardTopMusicWidget, TitleBarWidgetVisibility.Music, hidden);
        SetTitleBarWidgetVisibility(DashboardTitleBarCommunityWidget, TitleBarWidgetVisibility.Community, hidden);
        SetTitleBarWidgetVisibility(DashboardTitleBarSessionWidget, TitleBarWidgetVisibility.Session, hidden);
        SetTitleBarWidgetVisibility(DashboardCountdownModule, TitleBarWidgetVisibility.Countdown, hidden);
        SetTitleBarWidgetVisibility(DashboardConnectionSummaryChip, TitleBarWidgetVisibility.Connections, hidden);

        foreach (Border border in EnumerateLogicalBorders(DashboardTopStatusRow))
        {
            if (!Equals(border.Tag, "TitleBarChromeDivider")
                || string.IsNullOrWhiteSpace(border.Uid))
            {
                continue;
            }

            bool showDivider = !cardsEnabled
                && TitleBarWidgetVisibility.ShouldShowDividerBefore(hidden, border.Uid);
            border.Visibility = showDivider ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void SetTitleBarWidgetVisibility(
        FrameworkElement? element,
        string key,
        IReadOnlyList<string> hidden)
    {
        if (element is null)
        {
            return;
        }

        element.Visibility = TitleBarWidgetVisibility.IsVisible(hidden, key)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = LogicalTreeHelper.GetParent(current)
                ?? VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<Border> EnumerateLogicalBorders(DependencyObject? root)
    {
        if (root is null)
        {
            yield break;
        }

        if (root is Border border)
        {
            yield return border;
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
            {
                foreach (Border nested in EnumerateLogicalBorders(dependencyObject))
                {
                    yield return nested;
                }
            }
        }
    }

    private void OnThemeChanged()
    {
        if (TryFindResource("AppFontFamily") is FontFamily fontFamily)
        {
            FontFamily = fontFamily;
        }

        // Code-behind local values would otherwise pin Classic colors after a theme swap.
        DashboardConnectionSummaryChip?.ClearValue(Border.BackgroundProperty);
        DashboardConnectionSummaryChip?.ClearValue(Border.BorderBrushProperty);
        DashboardServiceStatusSection?.ClearValue(Border.BackgroundProperty);

        // Active-Nav nutzt Tag + DynamicResource; Theme-Swap aktualisiert Farben ohne Reapply.
        // Player/MultiPc hier mitführen, falls später lokale Werte gesetzt werden.
        Button? active = new Button?[]
        {
            DashboardButton, PlayerButton, ServicesButton, WorkflowButton, MultiPcButton,
            StatisticsButton, OverlaysButton, AlertsButton, SettingsButton, DiagnosticsButton,
            ServicesSpotifyButton, ServicesTwitchButton, ServicesObsButton,
            ServicesStreamerBotButton, ServicesStreamDeckButton
        }.FirstOrDefault(b => b is not null && Equals(b.Tag, "Active"));
        if (active is not null)
        {
            SetActiveNavigationButton(active);
        }
    }
}
