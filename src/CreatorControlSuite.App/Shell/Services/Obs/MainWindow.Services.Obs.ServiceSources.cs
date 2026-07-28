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
    private async Task RefreshServicesObsSceneItemsAsync()
    {
        int refreshVersion = ++_obsSceneItemsRefreshVersion;
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.ItemsSource = null; ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem = null; ServicesPageViewHost.ObsServiceViewHost.ServicesObsShowSceneItemButton.IsEnabled = false; ServicesPageViewHost.ObsServiceViewHost.ServicesObsHideSceneItemButton.IsEnabled = false;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsLockSceneItemButton.IsEnabled = false; ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnlockSceneItemButton.IsEnabled = false;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemUpButton.IsEnabled = false; ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemDownButton.IsEnabled = false; SetObsSceneItemTransformControlsEnabled(false); ClearObsSourceFilters("Zuerst eine Quelle auswählen."); ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedSceneItemStateText.Text = "Zuerst eine Szene auswählen."; return;
        }
        string? selectedSourceName = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem as ObsSceneItemInfo)?.SourceName;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedSceneItemStateText.Text = $"Quellen für „{scene.Name}“ werden geladen …";
        try
        {
            IReadOnlyList<ObsSceneItemInfo> items = await _obsClient.GetSceneItemListAsync(scene.Name);
            if (refreshVersion != _obsSceneItemsRefreshVersion || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo currentScene || !string.Equals(currentScene.Name, scene.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _servicesObsSceneItems = items;
            ApplyServicesObsSourceFilter();
            if (!string.IsNullOrWhiteSpace(selectedSourceName))
            {
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem = items.FirstOrDefault(item => string.Equals(item.SourceName, selectedSourceName, StringComparison.OrdinalIgnoreCase));
            }

            bool valid = ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is ObsSceneItemInfo; ServicesPageViewHost.ObsServiceViewHost.ServicesObsShowSceneItemButton.IsEnabled = valid; ServicesPageViewHost.ObsServiceViewHost.ServicesObsHideSceneItemButton.IsEnabled = valid;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsLockSceneItemButton.IsEnabled = valid; ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnlockSceneItemButton.IsEnabled = valid;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemUpButton.IsEnabled = valid; ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemDownButton.IsEnabled = valid; SetObsSceneItemTransformControlsEnabled(valid);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedSceneItemStateText.Text = $"{items.Count} Quellen in „{scene.Name}“";
        }
        catch (Exception exception)
        {
            if (refreshVersion != _obsSceneItemsRefreshVersion)
            {
                return;
            }

            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.ItemsSource = null; ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem = null; ServicesPageViewHost.ObsServiceViewHost.ServicesObsShowSceneItemButton.IsEnabled = false; ServicesPageViewHost.ObsServiceViewHost.ServicesObsHideSceneItemButton.IsEnabled = false;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsLockSceneItemButton.IsEnabled = false; ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnlockSceneItemButton.IsEnabled = false;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemUpButton.IsEnabled = false; ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemDownButton.IsEnabled = false; SetObsSceneItemTransformControlsEnabled(false); ClearObsSourceFilters("Filter konnten nicht geladen werden."); ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedSceneItemStateText.Text = $"Quellen konnten nicht geladen werden: {exception.Message}";
        }
    }
    private async Task RefreshSelectedObsSceneItemStateAsync()
    {
        bool valid = ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is ObsSceneItemInfo; ServicesPageViewHost.ObsServiceViewHost.ServicesObsShowSceneItemButton.IsEnabled = valid; ServicesPageViewHost.ObsServiceViewHost.ServicesObsHideSceneItemButton.IsEnabled = valid;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsLockSceneItemButton.IsEnabled = valid; ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnlockSceneItemButton.IsEnabled = valid;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemUpButton.IsEnabled = valid; ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemDownButton.IsEnabled = valid; SetObsSceneItemTransformControlsEnabled(valid);
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsRestartMediaButton.IsEnabled = false;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsStopMediaButton.IsEnabled = false;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshBrowserButton.IsEnabled = false; ClearObsSourceFilters("Quelle auswählen, um Filter zu laden."); if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is ObsSceneInfo scene)
            {
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedSceneItemStateText.Text = $"Quelle in „{scene.Name}“ auswählen.";
            }

            return;
        }
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsLockSceneItemButton.IsEnabled = !item.Locked;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnlockSceneItemButton.IsEnabled = item.Locked;
        int itemCount = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.ItemsSource as IEnumerable<ObsSceneItemInfo>)?.Count() ?? 0;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemUpButton.IsEnabled = item.Index < Math.Max(0, itemCount - 1);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemDownButton.IsEnabled = item.Index > 0;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedSceneItemStateText.Text = $"{item.SourceName}: {(item.Enabled ? "sichtbar" : "ausgeblendet")} · {(item.Locked ? "gesperrt" : "entsperrt")} · Ebene {item.Index}" + (item.IsGroup ? " · Gruppe" : string.Empty);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRestartMediaButton.IsEnabled = _obsClient.IsConnected && IsRestartableObsMediaSource(item.SourceType);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStopMediaButton.IsEnabled = _obsClient.IsConnected && IsRestartableObsMediaSource(item.SourceType);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshBrowserButton.IsEnabled = _obsClient.IsConnected && IsObsBrowserSource(item.SourceType);
        await LoadSelectedObsSceneItemTransformAsync(showNotification: false);
        await RefreshSelectedObsSourceFiltersAsync();
    }


    private void ApplyServicesObsSceneFilter()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList is null)
        {
            return;
        }

        string? selectedName = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem as ObsSceneInfo)?.Name;
        string search = ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneSearchBox?.Text?.Trim() ?? string.Empty;
        var filtered = _servicesObsScenes
            .Where(scene => string.IsNullOrWhiteSpace(search) || scene.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(scene => string.Equals(scene.Name, _servicesObsCurrentScene, StringComparison.OrdinalIgnoreCase))
            .ThenBy(scene => scene.Index)
            .ToList();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.ItemsSource = filtered;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem = filtered.FirstOrDefault(scene => string.Equals(scene.Name, selectedName, StringComparison.OrdinalIgnoreCase))
            ?? filtered.FirstOrDefault(scene => string.Equals(scene.Name, _servicesObsCurrentScene, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyServicesObsSourceFilter()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList is null)
        {
            return;
        }

        string? selectedName = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem as ObsSceneItemInfo)?.SourceName;
        string search = ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceSearchBox?.Text?.Trim() ?? string.Empty;
        var filtered = _servicesObsSceneItems
            .Where(item => string.IsNullOrWhiteSpace(search) || item.SourceName.Contains(search, StringComparison.OrdinalIgnoreCase) || item.SourceType.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.ItemsSource = filtered;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem = filtered.FirstOrDefault(item => string.Equals(item.SourceName, selectedName, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyServicesObsInputFilter()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList is null)
        {
            return;
        }

        string? selectedName = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem as ObsInputInfo)?.Name;
        string search = ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputSearchBox?.Text?.Trim() ?? string.Empty;
        string mode = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputFilterBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var filtered = _servicesObsInputs
            .Where(input => string.IsNullOrWhiteSpace(search) || input.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || input.Kind.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(input => mode switch
            {
                "muted" => IsObsInputMuted(input.Name),
                "all" => true,
                _ => string.Equals(ClassifyObsAudioInput(input), mode, StringComparison.OrdinalIgnoreCase)
            })
            .OrderBy(input => ClassifyObsAudioInput(input))
            .ThenBy(input => input.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.ItemsSource = filtered;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem = filtered.FirstOrDefault(input => string.Equals(input.Name, selectedName, StringComparison.OrdinalIgnoreCase)) ?? filtered.FirstOrDefault();
    }

    private static string ClassifyObsAudioInput(ObsInputInfo input)
    {
        string value = $"{input.Name} {input.Kind} {input.UnversionedKind}".ToLowerInvariant();
        if (value.Contains("mic") || value.Contains("mikro") || value.Contains("yeti") || value.Contains("rode") || value.Contains("voice"))
        {
            return "microphone";
        }

        if (value.Contains("spotify") || value.Contains("music") || value.Contains("musik"))
        {
            return "music";
        }

        if (value.Contains("browser") || value.Contains("alert") || value.Contains("streamelements"))
        {
            return "browser";
        }

        return "game";
    }

    private bool IsObsInputMuted(string inputName) =>
        _servicesObsInputsMuted.TryGetValue(inputName, out bool muted) && muted;

    private readonly Dictionary<string, bool> _servicesObsInputsMuted = new(StringComparer.OrdinalIgnoreCase);

    private void UpdateObsLiveMeters(IReadOnlyList<ObsInputVolumeMeter> meters)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (ObsInputVolumeMeter meter in meters)
        {
            _obsLiveMeters[meter.InputName] = meter;
            if (!_obsPeakHold.TryGetValue(meter.InputName, out (double PeakDb, DateTimeOffset At) held) || meter.PeakDb >= held.PeakDb || now - held.At > TimeSpan.FromSeconds(2))
            {
                _obsPeakHold[meter.InputName] = (meter.PeakDb, now);
            }
        }
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo selected || !_obsLiveMeters.TryGetValue(selected.Name, out ObsInputVolumeMeter? current))
        {
            return;
        }

        double heldPeak = _obsPeakHold.TryGetValue(selected.Name, out (double PeakDb, DateTimeOffset At) peak) ? peak.PeakDb : current.PeakDb;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsLiveMeterBar.Value = Math.Clamp(current.MagnitudeDb, -60, 10);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsLiveMeterText.Text = $"Live-Pegel: {current.MagnitudeDb:0.0} dB · Peak {current.PeakDb:0.0} dB";
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsPeakHoldText.Text = $"Peak-Hold: {heldPeak:0.0} dB" + (heldPeak >= -0.1 ? " · CLIPPING" : string.Empty);
    }

    private async Task SetObsInputsMuteAsync(IEnumerable<ObsInputInfo> inputs, bool muted, string label)
    {
        if (!_obsClient.IsConnected) { AddDashboardNotification("OBS ist nicht verbunden.", "Warnung"); return; }
        int applied = 0;
        foreach (ObsInputInfo input in inputs)
        {
            try { await _obsClient.SetInputMuteAsync(input.Name, muted); _servicesObsInputsMuted[input.Name] = muted; applied++; } catch { }
        }
        ApplyServicesObsInputFilter();
        AddDashboardNotification($"{label}: {applied} Quellen {(muted ? "gemutet" : "entmutet")}.", "Info");
    }

    private async Task SoloObsAudioCategoryAsync(string category)
    {
        foreach (ObsInputInfo input in _servicesObsInputs)
        {
            try
            {
                bool muted = !string.Equals(ClassifyObsAudioInput(input), category, StringComparison.OrdinalIgnoreCase);
                await _obsClient.SetInputMuteAsync(input.Name, muted);
                _servicesObsInputsMuted[input.Name] = muted;
            }
            catch { }
        }
        ApplyServicesObsInputFilter();
        AddDashboardNotification(category == "microphone" ? "Nur Mikrofone sind aktiv." : "Nur Spiel/Desktop ist aktiv.", "Info");
    }

    private string SelectedObsAudioGroup() => (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioGroupBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "game";

    private async Task SetSelectedObsAudioGroupMuteAsync(bool muted)
    {
        string group = SelectedObsAudioGroup();
        await SetObsInputsMuteAsync(_servicesObsInputs.Where(input => ClassifyObsAudioInput(input) == group), muted, "Audiogruppe");
    }

    private async Task ApplyObsAudioGroupVolumeAsync()
    {
        if (!double.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsGroupVolumeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double db))
        {
            AddDashboardNotification("Ungültiger Gruppenpegel.", "Warnung");
            return;
        }
        db = Math.Clamp(db, -100, 26);
        string group = SelectedObsAudioGroup();
        int applied = 0;
        foreach (ObsInputInfo? input in _servicesObsInputs.Where(input => ClassifyObsAudioInput(input) == group))
        {
            try { await _obsClient.SetInputVolumeDbAsync(input.Name, db); applied++; } catch { }
        }
        AddDashboardNotification($"Gruppenpegel auf {db:0.0} dB gesetzt ({applied} Quellen).", "Info");
        await RefreshSelectedObsInputStateAsync();
    }

    private static bool IsRestartableObsMediaSource(string sourceType) =>
        sourceType.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
        sourceType.Contains("vlc", StringComparison.OrdinalIgnoreCase) ||
        sourceType.Contains("media", StringComparison.OrdinalIgnoreCase);

    private static bool IsObsBrowserSource(string sourceType) =>
        sourceType.Contains("browser", StringComparison.OrdinalIgnoreCase);

    private async Task RestartSelectedObsMediaInputAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("Keine OBS-Medienquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.RestartMediaInputAsync(item.SourceName);
            AddDashboardNotification($"OBS-Medienquelle „{item.SourceName}“ wurde neu gestartet.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Medienquelle konnte nicht neu gestartet werden: {exception.Message}", "Fehler");
        }
    }

    private async Task StopSelectedObsMediaInputAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("Keine OBS-Medienquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.StopMediaInputAsync(item.SourceName);
            AddDashboardNotification($"OBS-Medienquelle „{item.SourceName}“ wurde gestoppt.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Medienquelle konnte nicht gestoppt werden: {exception.Message}", "Fehler");
        }
    }

    private async Task RefreshSelectedObsBrowserInputAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("Keine OBS-Browserquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.PressInputPropertiesButtonAsync(item.SourceName, "refreshnocache");
            AddDashboardNotification($"OBS-Browserquelle „{item.SourceName}“ wurde ohne Cache neu geladen.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Browserquelle konnte nicht neu geladen werden: {exception.Message}", "Fehler");
        }
    }

    private void ClearObsSourceFilters(string state)
    {
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.SelectedItem = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsEnableSourceFilterButton.IsEnabled = false;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsDisableSourceFilterButton.IsEnabled = false;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshSourceFiltersButton.IsEnabled = ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is ObsSceneItemInfo && _obsClient.IsConnected;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFilterStateText.Text = state;
    }

    private async Task RefreshSelectedObsSourceFiltersAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            ClearObsSourceFilters("Quelle auswählen, um Filter zu laden.");
            return;
        }

        string? selectedFilterName = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.SelectedItem as ObsSourceFilterInfo)?.Name;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshSourceFiltersButton.IsEnabled = true;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFilterStateText.Text = $"Filter für „{item.SourceName}“ werden geladen …";
        try
        {
            IReadOnlyList<ObsSourceFilterInfo> filters = await _obsClient.GetSourceFilterListAsync(item.SourceName);
            if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo current || !string.Equals(current.SourceName, item.SourceName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.ItemsSource = filters;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.SelectedItem = !string.IsNullOrWhiteSpace(selectedFilterName)
                ? filters.FirstOrDefault(filter => string.Equals(filter.Name, selectedFilterName, StringComparison.OrdinalIgnoreCase))
                : filters.FirstOrDefault();
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFilterStateText.Text = filters.Count == 0
                ? $"„{item.SourceName}“ hat keine Filter."
                : $"{filters.Count} Filter für „{item.SourceName}“ geladen.";
            RefreshSelectedObsSourceFilterState();
        }
        catch (Exception exception)
        {
            ClearObsSourceFilters($"Filter konnten nicht geladen werden: {exception.Message}");
        }
    }

    private void RefreshSelectedObsSourceFilterState()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.SelectedItem is not ObsSourceFilterInfo filter)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsEnableSourceFilterButton.IsEnabled = false;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsDisableSourceFilterButton.IsEnabled = false;
            return;
        }

        ServicesPageViewHost.ObsServiceViewHost.ServicesObsEnableSourceFilterButton.IsEnabled = !filter.Enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsDisableSourceFilterButton.IsEnabled = filter.Enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFilterStateText.Text = $"{filter.Name}: {(filter.Enabled ? "aktiv" : "deaktiviert")} · {filter.Kind}";
    }

    private async Task SetSelectedObsSourceFilterEnabledAsync(bool enabled)
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.SelectedItem is not ObsSourceFilterInfo filter)
        {
            AddDashboardNotification("OBS-Filter kann nicht geschaltet werden: Quelle oder Filter fehlt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.SetSourceFilterEnabledAsync(item.SourceName, filter.Name, enabled);
            await RefreshSelectedObsSourceFiltersAsync();
            AddDashboardNotification($"Filter „{filter.Name}“ wurde {(enabled ? "aktiviert" : "deaktiviert")}.", "Info");
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFilterStateText.Text = $"Filter konnte nicht geschaltet werden: {exception.Message}";
            AddDashboardNotification(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFilterStateText.Text, "Fehler");
        }
    }

    private async Task SetSelectedObsSceneItemVisibilityAsync(bool enabled)
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item) { AddDashboardNotification("OBS-Quelle kann nicht geschaltet werden: Szene oder Quelle fehlt.", "Warnung"); return; }
        try
        {
            IReadOnlyList<ObsSceneItemInfo> currentItems = await _obsClient.GetSceneItemListAsync(scene.Name);
            ObsSceneItemInfo? currentItem = currentItems.FirstOrDefault(candidate => string.Equals(candidate.SourceName, item.SourceName, StringComparison.OrdinalIgnoreCase));
            if (currentItem is null) { AddDashboardNotification($"OBS-Quelle „{item.SourceName}“ existiert in „{scene.Name}“ nicht mehr.", "Warnung"); await RefreshServicesObsSceneItemsAsync(); return; }
            await _obsClient.SetSceneItemEnabledAsync(scene.Name, currentItem.SourceName, enabled); await RefreshServicesObsSceneItemsAsync();
            AddDashboardNotification($"{currentItem.SourceName} wurde in {scene.Name} {(enabled ? "eingeblendet" : "ausgeblendet")}.", "Info");
        }
        catch (Exception exception) { AddDashboardNotification($"OBS-Quelle konnte nicht geschaltet werden: {exception.Message}", "Fehler"); }
    }

    private async Task SetSelectedObsSceneItemLockAsync(bool locked)
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("OBS-Quelle kann nicht gesperrt werden: Szene oder Quelle fehlt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.SetSceneItemLockedAsync(scene.Name, item.SourceName, locked);
            await RefreshServicesObsSceneItemsAsync();
            AddDashboardNotification($"{item.SourceName} wurde in {scene.Name} {(locked ? "gesperrt" : "entsperrt")}.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Quelle konnte nicht {(locked ? "gesperrt" : "entsperrt")} werden: {exception.Message}", "Fehler");
        }
    }

    private async Task MoveSelectedObsSceneItemAsync(int indexDelta)
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("OBS-Quelle kann nicht verschoben werden: Szene oder Quelle fehlt.", "Warnung");
            return;
        }

        try
        {
            IReadOnlyList<ObsSceneItemInfo> items = await _obsClient.GetSceneItemListAsync(scene.Name);
            ObsSceneItemInfo? current = items.FirstOrDefault(candidate => candidate.ItemId == item.ItemId)
                ?? items.FirstOrDefault(candidate => string.Equals(candidate.SourceName, item.SourceName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                await RefreshServicesObsSceneItemsAsync();
                AddDashboardNotification($"OBS-Quelle „{item.SourceName}“ existiert nicht mehr.", "Warnung");
                return;
            }

            int maximumIndex = Math.Max(0, items.Count - 1);
            int targetIndex = Math.Clamp(current.Index + indexDelta, 0, maximumIndex);
            if (targetIndex == current.Index)
            {
                return;
            }

            await _obsClient.SetSceneItemIndexAsync(scene.Name, current.SourceName, targetIndex);
            await RefreshServicesObsSceneItemsAsync();
            AddDashboardNotification($"{current.SourceName} wurde in {scene.Name} eine Ebene {(indexDelta > 0 ? "nach oben" : "nach unten")} verschoben.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Quelle konnte nicht verschoben werden: {exception.Message}", "Fehler");
        }
    }
}
