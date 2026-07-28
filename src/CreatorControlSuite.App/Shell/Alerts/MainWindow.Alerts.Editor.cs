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
    private sealed record AlertAudioOutputDevice(string ID, string FriendlyName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WaveOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;

        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
        public uint Support;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern uint waveOutGetDevCaps(
        UIntPtr deviceId,
        out WaveOutCaps capabilities,
        uint capabilitiesSize);

    private void LoadAlertAudioOutputDevices()
    {
        try
        {
            string? selected = AlertAudioOutputDeviceBox.SelectedValue?.ToString();
            var devices = new List<AlertAudioOutputDevice>
            {
                new("default", "Windows-Standardausgabe")
            };

            uint deviceCount = waveOutGetNumDevs();
            uint capsSize = (uint)Marshal.SizeOf<WaveOutCaps>();
            for (uint index = 0; index < deviceCount; index++)
            {
                if (waveOutGetDevCaps((UIntPtr)index, out WaveOutCaps capabilities, capsSize) == 0)
                {
                    string name = string.IsNullOrWhiteSpace(capabilities.ProductName)
                        ? $"Audioausgabe {index + 1}"
                        : capabilities.ProductName.Trim();
                    devices.Add(new AlertAudioOutputDevice($"waveout:{index}", name));
                }
            }

            AlertAudioOutputDeviceBox.ItemsSource = devices;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                AlertAudioOutputDeviceBox.SelectedValue = selected;
            }

            if (AlertAudioOutputDeviceBox.SelectedIndex < 0)
            {
                AlertAudioOutputDeviceBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            AlertAudioOutputDeviceBox.ItemsSource = new[]
            {
                new AlertAudioOutputDevice("default", "Windows-Standardausgabe")
            };
            AlertAudioOutputDeviceBox.SelectedIndex = 0;
            AlertPreviewStatusText.Text = "Audioausgänge konnten nicht vollständig eingelesen werden: " + ex.Message;
        }
    }

    private void LoadAlertAudioPreviewSource()
    {
        StopAlertAudioPreview();
        string path = AlertSoundPathBox.Text.Trim();
        if (!File.Exists(path))
        {
            return;
        }

        AlertAudioPreviewMedia.Source = new Uri(path, UriKind.Absolute);
    }

    private void PlaySelectedAlertAudioRange()
    {
        string path = AlertSoundPathBox.Text.Trim();
        if (!File.Exists(path))
        {
            AlertPreviewStatusText.Text = "Bitte zuerst eine vorhandene Audiodatei auswählen.";
            return;
        }
        if (AlertAudioPreviewMedia.Source is null)
        {
            LoadAlertAudioPreviewSource();
        }

        AlertAudioPreviewMedia.Position = TimeSpan.FromSeconds(AlertAudioStartSlider.Value);
        AlertAudioPreviewMedia.Volume = 1.0;
        AlertAudioPreviewMedia.Play();
        _alertAudioPreviewTimer.Start();
    }

    private void StopAlertAudioPreview()
    {
        _alertAudioPreviewTimer.Stop();
        AlertAudioPreviewMedia.Stop();
        AlertAudioPreviewMedia.Position = TimeSpan.FromSeconds(AlertAudioStartSlider.Value);
    }

    private static string FormatAlertAudioTime(double seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss\.fff");

    private void UpdateAlertAudioTrimLabels()
    {
        // ValueChanged can fire while InitializeComponent is still creating the XAML controls.
        // At that point one or more of these fields can legitimately still be null.
        if (AlertAudioStartText is null ||
            AlertAudioEndText is null ||
            AlertAudioStartSlider is null ||
            AlertAudioEndSlider is null)
        {
            return;
        }

        AlertAudioStartText.Text = "Start: " + FormatAlertAudioTime(AlertAudioStartSlider.Value);
        AlertAudioEndText.Text = "Ende: " + FormatAlertAudioTime(AlertAudioEndSlider.Value);
    }

    private void AlertAudioPreviewMedia_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (!AlertAudioPreviewMedia.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        double duration = Math.Max(0.1, AlertAudioPreviewMedia.NaturalDuration.TimeSpan.TotalSeconds);
        _updatingAlertAudioTrimUi = true;
        AlertAudioStartSlider.Maximum = duration;
        AlertAudioEndSlider.Maximum = duration;
        if (AlertAudioEndSlider.Value <= AlertAudioStartSlider.Value || AlertAudioEndSlider.Value <= 1)
        {
            AlertAudioEndSlider.Value = duration;
        }

        _updatingAlertAudioTrimUi = false;
        UpdateAlertAudioTrimLabels();
    }

    private void AlertAudioTrimSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingAlertAudioTrimUi || AlertAudioStartSlider is null || AlertAudioEndSlider is null)
        {
            return;
        }

        _updatingAlertAudioTrimUi = true;
        if (AlertAudioStartSlider.Value > AlertAudioEndSlider.Value)
        {
            if (ReferenceEquals(sender, AlertAudioStartSlider))
            {
                AlertAudioEndSlider.Value = AlertAudioStartSlider.Value;
            }
            else
            {
                AlertAudioStartSlider.Value = AlertAudioEndSlider.Value;
            }
        }
        _updatingAlertAudioTrimUi = false;
        UpdateAlertAudioTrimLabels();
    }

    private async Task LoadSelectedAlertDefinitionAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type ||
            !_settings.Alerts.Definitions.TryGetValue(
                type,
                out AlertDefinitionSettings? definition))
        {
            return;
        }

        _alertDefinitionEditorViewModel.Load(definition);
        LoadAlertAudioOutputDevices();
        LoadAlertAudioPreviewSource();

        await PreviewAlertAsync();
    }

    private async Task SaveSelectedAlertDefinitionAsync()
    {
        try
        {
            SaveAlertDefinitionToSettings();

            await _settingsStore.SaveAsync(_settings);

            _alertLibraryPageViewModel.Load(
                _settings,
                AlertTypeBox.SelectedItem as string);
            AlertTypeBox.SelectedItem =
                _alertLibraryPageViewModel.SelectedType;
            AlertPreviewStatusText.Text =
                "Alert gespeichert.";

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            AlertPreviewStatusText.Text =
                exception.Message;

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void SaveAlertDefinitionToSettings()
    {
        if (AlertTypeBox.SelectedItem is not string type ||
            !_settings.Alerts.Definitions.TryGetValue(
                type,
                out AlertDefinitionSettings? definition))
        {
            return;
        }

        if (!_alertDefinitionEditorViewModel.TryApplyTo(
                definition,
                out string error))
        {
            throw new InvalidOperationException(error);
        }
    }

    private async Task PreviewAlertAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type)
        {
            return;
        }

        try
        {
            SaveAlertDefinitionToSettings();

            IReadOnlyDictionary<string, string> variables = CreateAlertTestVariables(type);

            AlertPreview preview = await _alertsModule.BuildPreviewAsync(
                type,
                AlertTestUserBox.Text.Trim(),
                variables);

            AlertPreviewTypeText.Text =
                preview.Type.ToUpperInvariant();

            AlertPreviewMessageText.Text =
                preview.Text;

            AlertPreviewMessageText.FontFamily =
                new System.Windows.Media.FontFamily(
                    preview.FontFace);

            AlertPreviewMessageText.FontSize =
                preview.FontSize;

            AlertPreviewMessageText.Foreground =
                new System.Windows.Media.BrushConverter()
                    .ConvertFromString(
                        preview.FontColor)
                as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.White;

            AlertPreviewMedia.Stop();
            AlertPreviewMedia.Source = null;

            if (!string.IsNullOrWhiteSpace(
                    preview.MediaPath) &&
                File.Exists(preview.MediaPath))
            {
                AlertPreviewMedia.Source =
                    new Uri(
                        preview.MediaPath,
                        UriKind.Absolute);

                AlertPreviewMedia.Position =
                    TimeSpan.Zero;

                AlertPreviewMedia.Play();
            }

            AlertPreviewStatusText.Text =
                "Vorschau aktualisiert.";

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            AlertPreviewStatusText.Text =
                exception.Message;

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task TestAlertInObsAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type)
        {
            return;
        }

        try
        {
            SaveAlertDefinitionToSettings();
            await _settingsStore.SaveAsync(_settings);

            IReadOnlyDictionary<string, string> variables = CreateAlertTestVariables(type);

            await _alertsModule.EnqueueAsync(
                type,
                AlertTestUserBox.Text.Trim(),
                variables,
                _settings.Alerts.Definitions[type].Priority);

            AlertPreviewStatusText.Text =
                "Alert wurde in die OBS-Queue eingereiht.";

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            AlertPreviewStatusText.Text =
                exception.Message;

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            MessageBox.Show(
                exception.Message,
                "Alert-Test fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task InstallObsAlertSceneAsync()
    {
        string? type = AlertTypeBox.SelectedItem as string
                   ?? _settings.Alerts.Definitions.Keys.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(type))
        {
            MessageBox.Show(
                "Bitte zuerst einen Alert anlegen oder auswählen.",
                "OBS Alert-Szene",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            SaveAlertDefinitionToSettings();
            if (!_alertRuntimePageViewModel.TryApplyTo(
                    _settings.Alerts,
                    _settings.StreamerBot,
                    out string settingsError))
            {
                throw new InvalidOperationException(settingsError);
            }
            await _settingsStore.SaveAsync(_settings);

            IReadOnlyDictionary<string, string> variables = CreateAlertTestVariables(type);
            string user = string.IsNullOrWhiteSpace(AlertTestUserBox.Text)
                ? "TestUser"
                : AlertTestUserBox.Text.Trim();

            await _alertsModule.InstallObsSourcesAsync(
                type,
                user,
                variables);

            _alertRuntimePageViewModel.SetInstallStatus(
                $"OBS-Szene '{_settings.Alerts.ObsSceneName}' mit Text- und Medienquelle angelegt.");

            MessageBox.Show(
                $"Die Szene '{_settings.Alerts.ObsSceneName}' wurde in OBS angelegt " +
                $"(Quellen: {_settings.Alerts.ObsTextSourceName}, {_settings.Alerts.ObsMediaSourceName}).",
                "OBS Alert-Szene",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _alertRuntimePageViewModel.SetInstallStatus(
                exception.Message);

            MessageBox.Show(
                exception.Message,
                "OBS Alert-Szene fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static IReadOnlyDictionary<string, string>
        CreateAlertTestVariables(string type)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        switch (type)
        {
            case "Raid":
                values["viewers"] = "25";
                break;

            case "Cheer":
                values["bits"] = "500";
                break;

            case "GiftSub":
                values["count"] = "5";
                break;

            case "ReSub":
                values["months"] = "12";
                break;
        }

        return values;
    }
}
