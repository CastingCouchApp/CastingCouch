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
    private bool EnsureDefaultDashboardSceneButtons()
    {
        if (_settings.Dashboard.SceneButtons.Count > 0)
        {
            return false;
        }

        var defaults = new (string Scene, string Emoji)[]
        {
            (_settings.Obs.StartScene, "🚀"),
            (_settings.Obs.LiveScene, "🎮"),
            (_settings.Obs.PauseScene, "☕"),
            (_settings.Obs.EndScene, "🏁"),
        };

        foreach ((string? scene, string? emoji) in defaults)
        {
            if (string.IsNullOrWhiteSpace(scene))
            {
                continue;
            }

            if (_settings.Dashboard.SceneButtons.Any(button =>
                    string.Equals(button.SceneName, scene, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _settings.Dashboard.SceneButtons.Add(new DashboardSceneButtonSettings
            {
                Title = scene,
                SceneName = scene,
                IconKind = "Emoji",
                IconValue = emoji
            });
        }

        return _settings.Dashboard.SceneButtons.Count > 0;
    }

    private void RebuildDashboardSceneButtons()
    {
        DashboardPageViewHost.DashboardSceneButtonsPanel.Children.Clear();

        foreach (DashboardSceneButtonSettings? settings in _settings.Dashboard.SceneButtons.ToList())
        {
            Button button = CreateDashboardSceneButton(settings);
            DashboardPageViewHost.DashboardSceneButtonsPanel.Children.Add(button);
        }

        HighlightDashboardSceneButtons(_servicesObsCurrentScene);
    }

    private Button CreateDashboardSceneButton(DashboardSceneButtonSettings settings)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        FrameworkElement? iconElement = CreateDashboardSceneButtonIcon(settings);
        if (iconElement is not null)
        {
            content.Children.Add(iconElement);
        }

        content.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(settings.Title) ? settings.SceneName : settings.Title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = iconElement is null ? new Thickness(0) : new Thickness(8, 0, 0, 0)
        });

        var button = new Button
        {
            Content = content,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 8, 12, 8),
            MinWidth = 96,
            Tag = settings,
            ToolTip = $"Zur OBS-Szene wechseln: {settings.SceneName}"
        };

        button.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                button,
                $"OBS-Szene: {settings.Title}",
                () => SwitchDashboardSceneByNameAsync(settings.SceneName));

        var contextMenu = new ContextMenu();
        var editItem = new MenuItem { Header = "Bearbeiten" };
        editItem.Click += async (_, _) => await EditDashboardSceneButtonAsync(settings);
        var deleteItem = new MenuItem { Header = "Löschen" };
        deleteItem.Click += async (_, _) => await DeleteDashboardSceneButtonAsync(settings);
        contextMenu.Items.Add(editItem);
        contextMenu.Items.Add(deleteItem);
        button.ContextMenu = contextMenu;

        return button;
    }

    private static FrameworkElement? CreateDashboardSceneButtonIcon(DashboardSceneButtonSettings settings)
    {
        string kind = settings.IconKind?.Trim() ?? "Emoji";
        if (string.Equals(kind, "Image", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(settings.IconValue) || !File.Exists(settings.IconValue))
                {
                    return null;
                }

                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(settings.IconValue, UriKind.Absolute);
                bitmap.DecodePixelWidth = 20;
                bitmap.EndInit();
                bitmap.Freeze();
                return new Image
                {
                    Source = bitmap,
                    Width = 18,
                    Height = 18,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            catch
            {
                return null;
            }
        }

        if (string.Equals(kind, "Glyph", StringComparison.OrdinalIgnoreCase))
        {
            return new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(settings.IconValue) ? "\uE714" : settings.IconValue,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(settings.IconValue) ? "🎬" : settings.IconValue,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void HighlightDashboardSceneButtons(string? currentScene)
    {
        Brush accent = _themeService.GetBrush("AccentBrush")
            ?? new SolidColorBrush(Color.FromRgb(255, 140, 0));
        SolidColorBrush transparent = Brushes.Transparent;

        foreach (object? child in DashboardPageViewHost.DashboardSceneButtonsPanel.Children)
        {
            if (child is not Button button || button.Tag is not DashboardSceneButtonSettings settings)
            {
                continue;
            }

            bool isActive = !string.IsNullOrWhiteSpace(currentScene) &&
                string.Equals(settings.SceneName, currentScene, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = isActive ? accent : transparent;
            button.BorderThickness = new Thickness(isActive ? 2 : 1);
            button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private async Task AddDashboardSceneButtonAsync()
    {
        IReadOnlyList<string> scenes = await GetDashboardSceneChoicesAsync();
        if (scenes.Count == 0)
        {
            AddDashboardNotification(
                "Keine OBS-Szenen verfügbar. Bitte zuerst OBS verbinden.",
                "Warnung");
            return;
        }

        var editor = new DashboardSceneButtonEditorWindow(scenes, assetStore: _overlayModule.AssetStore)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _settings.Dashboard.SceneButtons.Add(editor.Result);
        await _settingsStore.SaveAsync(_settings);
        RebuildDashboardSceneButtons();
    }

    private async Task EditDashboardSceneButtonAsync(DashboardSceneButtonSettings settings)
    {
        var scenes = (await GetDashboardSceneChoicesAsync()).ToList();
        if (scenes.Count == 0)
        {
            AddDashboardNotification(
                "Keine OBS-Szenen verfügbar. Bitte zuerst OBS verbinden.",
                "Warnung");
            return;
        }

        if (!scenes.Any(scene => string.Equals(scene, settings.SceneName, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(settings.SceneName))
        {
            scenes.Add(settings.SceneName);
        }

        var editor = new DashboardSceneButtonEditorWindow(scenes, settings, _overlayModule.AssetStore)
        {
            Owner = this
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        settings.Title = editor.Result.Title;
        settings.SceneName = editor.Result.SceneName;
        settings.IconKind = editor.Result.IconKind;
        settings.IconValue = editor.Result.IconValue;
        await _settingsStore.SaveAsync(_settings);
        RebuildDashboardSceneButtons();
    }

    private async Task DeleteDashboardSceneButtonAsync(DashboardSceneButtonSettings settings)
    {
        MessageBoxResult result = MessageBox.Show(
            $"Button „{settings.Title}“ wirklich löschen?",
            "Szenen-Button",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Dashboard.SceneButtons.RemoveAll(button =>
            string.Equals(button.Id, settings.Id, StringComparison.Ordinal));
        await _settingsStore.SaveAsync(_settings);
        RebuildDashboardSceneButtons();
    }

    private async Task<IReadOnlyList<string>> GetDashboardSceneChoicesAsync()
    {
        if (_obsClient.IsConnected)
        {
            try
            {
                IReadOnlyList<ObsSceneInfo> scenes = await _obsClient.GetSceneListAsync();
                var names = scenes
                    .Select(scene => scene.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (names.Count > 0)
                {
                    _dashboardObsSceneNames = names;
                    return names;
                }
            }
            catch (Exception exception)
            {
                _appLogger.Write(
                    AppLogLevel.Warning,
                    "OBS",
                    "OBS-Szenenliste für Button-Editor konnte nicht geladen werden.",
                    exception);
            }
        }

        if (_dashboardObsSceneNames.Count > 0)
        {
            return _dashboardObsSceneNames;
        }

        return [.. new[]
            {
                _settings.Obs.StartScene,
                _settings.Obs.LiveScene,
                _settings.Obs.PauseScene,
                _settings.Obs.EndScene
            }
            .Concat(_settings.AdditionalScenes)
            .Where(scene => !string.IsNullOrWhiteSpace(scene))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private async Task SwitchDashboardSceneByNameAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || !_obsClient.IsConnected)
        {
            AddDashboardNotification(
                "OBS ist nicht verbunden oder es wurde keine Szene ausgewählt.",
                "Warnung");
            return;
        }

        await _obsClient.SetCurrentProgramSceneAsync(sceneName);
        DashboardPageViewHost.DashboardCurrentSceneText.Text = sceneName;
        _servicesObsCurrentScene = sceneName;
        HighlightDashboardSceneButtons(sceneName);
        await RefreshDashboardObsScenePreviewAsync(sceneName);
        AddDashboardNotification($"OBS-Szene gewechselt: {sceneName}", "Info");
    }

    private void AddDashboardViewerTrendSample(int viewers)
    {
        _dashboardViewerTrendSamples.Enqueue(Math.Max(0, viewers));

        while (_dashboardViewerTrendSamples.Count > 48)
        {
            _dashboardViewerTrendSamples.Dequeue();
        }

        DashboardViewerTrendLine.Points.Clear();
        int[] samples = [.. _dashboardViewerTrendSamples];

        if (samples.Length == 0)
        {
            return;
        }

        const double width = 260;
        const double height = 28;
        int maximum = Math.Max(1, samples.Max());

        for (int index = 0; index < samples.Length; index++)
        {
            double x = samples.Length == 1
                ? 0
                : width * index / (samples.Length - 1);
            double y = height - (height * samples[index] / maximum);
            DashboardViewerTrendLine.Points.Add(new Point(x, y));
        }
    }
}
