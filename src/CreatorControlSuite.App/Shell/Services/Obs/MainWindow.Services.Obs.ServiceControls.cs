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
    private void SetObsSceneItemTransformControlsEnabled(bool enabled)
    {
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemXBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemYBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemWidthBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemHeightBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemRotationBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropLeftBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropTopBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropRightBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropBottomBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplySceneItemTransformButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsReloadSceneItemTransformButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsResetSceneItemTransformButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemFullscreenButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCentered720Button.IsEnabled = enabled;
    }

    private static bool TryParseObsTransformValue(string? value, out double result)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out result)
            || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private async Task LoadSelectedObsSceneItemTransformAsync(bool showNotification = true)
    {
        if (!_obsClient.IsConnected
            || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene
            || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            return;
        }

        try
        {
            ObsSceneItemTransformInfo transform = await _obsClient.GetSceneItemTransformAsync(scene.Name, item.SourceName);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemXBox.Text = transform.PositionX.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemYBox.Text = transform.PositionY.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemWidthBox.Text = transform.Width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemHeightBox.Text = transform.Height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemRotationBox.Text = transform.Rotation.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropLeftBox.Text = transform.CropLeft.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropTopBox.Text = transform.CropTop.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropRightBox.Text = transform.CropRight.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropBottomBox.Text = transform.CropBottom.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (showNotification)
            {
                AddDashboardNotification($"Transformation von {item.SourceName} wurde aus OBS geladen.", "Info");
            }
        }
        catch (Exception exception)
        {
            if (showNotification)
            {
                AddDashboardNotification($"Transformation konnte nicht geladen werden: {exception.Message}", "Fehler");
            }
        }
    }

    private async Task ResetSelectedObsSceneItemTransformAsync()
    {
        if (!_obsClient.IsConnected
            || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene
            || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            return;
        }

        try
        {
            await _obsClient.ResetSceneItemTransformAsync(scene.Name, item.SourceName);
            await LoadSelectedObsSceneItemTransformAsync(showNotification: false);
            AddDashboardNotification($"Transformation von {item.SourceName} wurde in OBS zurückgesetzt.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Transformation konnte nicht zurückgesetzt werden: {exception.Message}", "Fehler");
        }
    }

    private async Task ApplySelectedObsSceneItemTransformAsync()
    {
        if (!TryParseObsTransformValue(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemXBox.Text, out double x)
            || !TryParseObsTransformValue(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemYBox.Text, out double y)
            || !TryParseObsTransformValue(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemWidthBox.Text, out double width)
            || !TryParseObsTransformValue(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemHeightBox.Text, out double height)
            || !TryParseObsTransformValue(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemRotationBox.Text, out double rotation)
            || !int.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropLeftBox.Text, out int cropLeft)
            || !int.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropTopBox.Text, out int cropTop)
            || !int.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropRightBox.Text, out int cropRight)
            || !int.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropBottomBox.Text, out int cropBottom))
        {
            AddDashboardNotification("Transformation enthält ungültige Zahlen.", "Warnung");
            return;
        }

        if (width < 1 || height < 1 || width > 16384 || height > 16384)
        {
            AddDashboardNotification("Breite und Höhe müssen zwischen 1 und 16384 Pixeln liegen.", "Warnung");
            return;
        }
        if (rotation < -3600 || rotation > 3600 || new[] { cropLeft, cropTop, cropRight, cropBottom }.Any(value => value < 0 || value > 16384))
        {
            AddDashboardNotification("Drehung oder Zuschnitt liegt außerhalb des gültigen Bereichs.", "Warnung");
            return;
        }

        await ApplyObsSceneItemTransformAsync(x, y, width, height, rotation, cropLeft, cropTop, cropRight, cropBottom);
    }

    private async Task ApplyObsSceneItemTransformPresetAsync(double x, double y, double width, double height)
    {
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemXBox.Text = x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemYBox.Text = y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemWidthBox.Text = width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemHeightBox.Text = height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemRotationBox.Text = "0";
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropLeftBox.Text = "0";
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropTopBox.Text = "0";
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropRightBox.Text = "0";
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCropBottomBox.Text = "0";
        await ApplyObsSceneItemTransformAsync(x, y, width, height, 0, 0, 0, 0, 0);
    }

    private async Task ApplyObsSceneItemTransformAsync(double x, double y, double width, double height, double rotation, int cropLeft, int cropTop, int cropRight, int cropBottom)
    {
        if (!_obsClient.IsConnected
            || ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene
            || ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("OBS-Quelle kann nicht transformiert werden: Szene oder Quelle fehlt.", "Warnung");
            return;
        }

        try
        {
            IReadOnlyList<ObsSceneItemInfo> currentItems = await _obsClient.GetSceneItemListAsync(scene.Name);
            ObsSceneItemInfo? current = currentItems.FirstOrDefault(candidate => candidate.ItemId == item.ItemId)
                ?? currentItems.FirstOrDefault(candidate => string.Equals(candidate.SourceName, item.SourceName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                await RefreshServicesObsSceneItemsAsync();
                AddDashboardNotification($"OBS-Quelle „{item.SourceName}“ existiert nicht mehr.", "Warnung");
                return;
            }

            await _obsClient.SetSceneItemDetailedTransformAsync(scene.Name, current.SourceName, x, y, width, height, rotation, cropLeft, cropTop, cropRight, cropBottom);
            AddDashboardNotification($"{current.SourceName}: Transformation übernommen (Position {x:0.#}/{y:0.#}, Größe {width:0.#} × {height:0.#}, Drehung {rotation:0.#}°).", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Quelle konnte nicht transformiert werden: {exception.Message}", "Fehler");
        }
    }

    private async Task SwitchServicesObsSceneAsync()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene)
        {
            return;
        }

        await _obsClient.SetCurrentProgramSceneAsync(scene.Name);
        await RefreshObsAsync();
        await RefreshServicesObsSceneItemsAsync();
    }

    private async Task RefreshDashboardObsAudioStateAsync()
    {
        if (!_obsClient.IsConnected ||
            DashboardPageViewHost.DashboardObsAudioInputBox.SelectedItem is not ObsInputInfo input)
        {
            DashboardPageViewHost.DashboardObsAudioStateText.Text = "Audioquelle auswählen";
            return;
        }

        try
        {
            ObsInputAudioState state = await _obsClient.GetInputAudioStateAsync(input.Name);
            DashboardPageViewHost.DashboardObsAudioStateText.Text =
                $"{state.Name}: {(state.Muted ? "GEMUTET" : "AKTIV")} · {state.VolumeDb:0.0} dB";
            DashboardPageViewHost.DashboardObsAudioVolumeBox.Text =
                state.VolumeDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            DashboardPageViewHost.DashboardObsAudioStateText.Text =
                "Diese OBS-Quelle besitzt keine steuerbaren Audioeigenschaften.";
        }
    }

    private async Task SetDashboardObsAudioMuteAsync(bool muted)
    {
        if (!_obsClient.IsConnected ||
            DashboardPageViewHost.DashboardObsAudioInputBox.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("OBS-Audio kann nicht gesteuert werden: keine verbundene Audioquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.SetInputMuteAsync(input.Name, muted);
            await RefreshDashboardObsAudioStateAsync();
            AddDashboardNotification(
                $"{input.Name} wurde {(muted ? "gemutet" : "aktiviert")}.",
                "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification(
                $"OBS-Audiofehler bei {input.Name}: {exception.Message}",
                "Fehler");
        }
    }

    private async Task SetDashboardObsAudioVolumeAsync()
    {
        if (!_obsClient.IsConnected ||
            DashboardPageViewHost.DashboardObsAudioInputBox.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("OBS-Lautstärke kann nicht gesetzt werden: keine Audioquelle ausgewählt.", "Warnung");
            return;
        }

        if (!double.TryParse(
                DashboardPageViewHost.DashboardObsAudioVolumeBox.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double db))
        {
            AddDashboardNotification("Ungültiger dB-Wert für den OBS-Audiomixer.", "Warnung");
            return;
        }

        db = Math.Clamp(db, -100, 26);
        try
        {
            await _obsClient.SetInputVolumeDbAsync(input.Name, db);
            await RefreshDashboardObsAudioStateAsync();
            AddDashboardNotification($"{input.Name}: Lautstärke auf {db:0.0} dB gesetzt.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification(
                $"OBS-Lautstärke konnte nicht gesetzt werden: {exception.Message}",
                "Fehler");
        }
    }


    private async Task ApplySelectedObsTransitionAsync()
    {
        if (!_obsClient.IsConnected)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = "OBS ist nicht verbunden.";
            AddDashboardNotification("OBS-Übergang kann nicht gesetzt werden: OBS ist nicht verbunden.", "Warnung");
            return;
        }

        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionBox.SelectedItem is not ObsTransitionInfo transition)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = "Bitte zuerst einen OBS-Übergang auswählen.";
            AddDashboardNotification("Bitte zuerst einen OBS-Übergang auswählen.", "Warnung");
            return;
        }

        if (!int.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionDurationBox.Text.Trim(), out int durationMilliseconds))
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = "Die Übergangsdauer muss eine ganze Zahl sein.";
            AddDashboardNotification("Ungültige OBS-Übergangsdauer.", "Warnung");
            return;
        }

        durationMilliseconds = Math.Clamp(durationMilliseconds, 0, 20000);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionDurationBox.Text = durationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyTransitionButton.IsEnabled = false;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = $"„{transition.Name}“ wird angewendet …";

        try
        {
            await _obsClient.SetCurrentSceneTransitionAsync(transition.Name);
            await _obsClient.SetCurrentSceneTransitionDurationAsync(durationMilliseconds);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = $"Aktiv: {transition.Name} · {durationMilliseconds} ms";
            AddDashboardNotification($"OBS-Übergang „{transition.Name}“ mit {durationMilliseconds} ms übernommen.", "Info");
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = $"Übergang konnte nicht gesetzt werden: {exception.Message}";
            AddDashboardNotification($"OBS-Übergang konnte nicht gesetzt werden: {exception.Message}", "Fehler");
        }
        finally
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyTransitionButton.IsEnabled = _obsClient.IsConnected;
        }
    }

    private int _obsInputStateRefreshVersion;
    private bool _updatingObsMixerVolumeUi;
    private void SetServicesObsAudioControlsEnabled(bool enabled)
    {
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMuteInputButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnmuteInputButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeDbBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeSlider.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSetVolumeButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeMinus20Button.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeMinus10Button.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeZeroButton.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMonitoringBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSyncOffsetBox.IsEnabled = enabled;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyAdvancedAudioButton.IsEnabled = enabled;
    }
    private static double DbToPercent(double db)
    {
        if (db <= -60)
        {
            return 0;
        }

        double multiplier = Math.Pow(10, db / 20.0);
        return Math.Clamp(multiplier * 100.0, 0, 316);
    }

    private async Task RefreshSelectedObsInputStateAsync()
    {
        int refreshVersion = ++_obsInputStateRefreshVersion;
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo input) { SetServicesObsAudioControlsEnabled(false); ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedInputStateText.Text = "Audioquelle auswählen"; return; }
        SetServicesObsAudioControlsEnabled(false); ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedInputStateText.Text = $"{input.Name}: Status wird geladen …";
        try
        {
            ObsInputAudioState state = await _obsClient.GetInputAudioStateAsync(input.Name);
            ObsInputAdvancedAudioState advancedState = await _obsClient.GetInputAdvancedAudioStateAsync(input.Name);
            if (refreshVersion != _obsInputStateRefreshVersion || ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo currentInput || !string.Equals(currentInput.Name, input.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _servicesObsInputsMuted[state.Name] = state.Muted;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedInputStateText.Text = $"{state.Name}: {(state.Muted ? "GEMUTET" : "AKTIV")} · {state.VolumeDb:0.0} dB · Sync {advancedState.SyncOffsetMilliseconds} ms";
            _updatingObsMixerVolumeUi = true;
            try
            {
                double sliderValue = Math.Clamp(state.VolumeDb, ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeSlider.Minimum, ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeSlider.Maximum);
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeSlider.Value = sliderValue;
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeDbBox.Text = state.VolumeDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsSyncOffsetBox.Text = advancedState.SyncOffsetMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (ComboBoxItem item in ServicesPageViewHost.ObsServiceViewHost.ServicesObsMonitoringBox.Items.OfType<ComboBoxItem>())
                {
                    if (string.Equals(item.Tag?.ToString(), advancedState.MonitorType, StringComparison.OrdinalIgnoreCase))
                    {
                        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMonitoringBox.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _updatingObsMixerVolumeUi = false;
            }
            SetServicesObsAudioControlsEnabled(true);
        }
        catch (Exception exception) { if (refreshVersion != _obsInputStateRefreshVersion) { return; } SetServicesObsAudioControlsEnabled(false); ServicesPageViewHost.ObsServiceViewHost.ServicesObsSelectedInputStateText.Text = $"Keine steuerbaren Audioeigenschaften: {exception.Message}"; }
    }
    private async Task SetSelectedObsInputMuteAsync(bool muted)
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo input) { AddDashboardNotification("OBS-Audio kann nicht gesteuert werden: keine gültige Audioquelle ausgewählt.", "Warnung"); return; }
        try { await _obsClient.SetInputMuteAsync(input.Name, muted); await RefreshSelectedObsInputStateAsync(); AddDashboardNotification($"{input.Name} wurde {(muted ? "gemutet" : "aktiviert")}.", "Info"); }
        catch (Exception exception) { AddDashboardNotification($"OBS-Audiofehler bei {input.Name}: {exception.Message}", "Fehler"); await RefreshSelectedObsInputStateAsync(); }
    }
    private async Task SetSelectedObsInputVolumeAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo input) { AddDashboardNotification("OBS-Lautstärke kann nicht gesetzt werden: keine gültige Audioquelle ausgewählt.", "Warnung"); return; }
        if (!double.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeDbBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double db)) { AddDashboardNotification("Ungültige OBS-Lautstärke. Bitte einen dB-Wert zwischen -100 und 26 eingeben.", "Warnung"); await RefreshSelectedObsInputStateAsync(); return; }
        db = Math.Clamp(db, -100, 26);
        try { await _obsClient.SetInputVolumeDbAsync(input.Name, db); await RefreshSelectedObsInputStateAsync(); AddDashboardNotification($"{input.Name}: Lautstärke auf {db:0.0} dB gesetzt.", "Info"); }
        catch (Exception exception) { AddDashboardNotification($"OBS-Lautstärke konnte für {input.Name} nicht gesetzt werden: {exception.Message}", "Fehler"); await RefreshSelectedObsInputStateAsync(); }
    }

    private async Task ApplyObsMixerPresetAsync(double db)
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("OBS-Pegel kann nicht gesetzt werden: keine Audioquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            SetServicesObsAudioControlsEnabled(false);
            await _obsClient.SetInputVolumeDbAsync(input.Name, db);
            await RefreshSelectedObsInputStateAsync();
            AddDashboardNotification($"{input.Name}: Schnellpegel {db:0} dB übernommen.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Schnellpegel konnte nicht gesetzt werden: {exception.Message}", "Fehler");
            await RefreshSelectedObsInputStateAsync();
        }
    }


    private async Task ApplySelectedObsAdvancedAudioAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("Erweiterte OBS-Audioeinstellungen können nicht gesetzt werden: keine Audioquelle ausgewählt.", "Warnung");
            return;
        }

        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsMonitoringBox.SelectedItem is not ComboBoxItem monitoringItem || string.IsNullOrWhiteSpace(monitoringItem.Tag?.ToString()))
        {
            AddDashboardNotification("Bitte einen Monitoring-Modus auswählen.", "Warnung");
            return;
        }

        if (!int.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsSyncOffsetBox.Text, out int syncOffsetMilliseconds))
        {
            AddDashboardNotification("Der Audio-Sync-Wert muss eine ganze Millisekunden-Zahl sein.", "Warnung");
            return;
        }

        syncOffsetMilliseconds = Math.Clamp(syncOffsetMilliseconds, -950, 20000);
        try
        {
            SetServicesObsAudioControlsEnabled(false);
            await _obsClient.SetInputAudioMonitorTypeAsync(input.Name, monitoringItem.Tag!.ToString()!);
            await _obsClient.SetInputAudioSyncOffsetAsync(input.Name, syncOffsetMilliseconds);
            await RefreshSelectedObsInputStateAsync();
            AddDashboardNotification($"{input.Name}: Monitoring und Audio-Sync wurden übernommen.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Erweiterte OBS-Audioeinstellungen konnten nicht gesetzt werden: {exception.Message}", "Fehler");
            await RefreshSelectedObsInputStateAsync();
        }
    }


    private void RefreshObsAudioProfilesUi(string? selectedName = null)
    {
        _settings.Obs.AudioProfiles ??= [];
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.ItemsSource = _settings.Obs.AudioProfiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        ObsAudioProfileSettings? selected = _settings.Obs.AudioProfiles.FirstOrDefault(profile => string.Equals(profile.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.SelectedItem = selected;
        }
        else if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.Items.Count > 0)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.SelectedIndex = 0;
        }

        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyAudioProfileButton.IsEnabled = _obsClient.IsConnected && ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.Items.Count > 0;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsDeleteAudioProfileButton.IsEnabled = ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.Items.Count > 0;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSaveAudioProfileButton.IsEnabled = _obsClient.IsConnected;
    }

    private async Task SaveObsAudioProfileAsync()
    {
        if (!_obsClient.IsConnected)
        {
            AddDashboardNotification("Audio-Profil kann nicht gespeichert werden: OBS ist nicht verbunden.", "Warnung");
            return;
        }
        string? name = ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AddDashboardNotification("Bitte einen Namen für das Audio-Profil eingeben.", "Warnung");
            return;
        }
        try
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileStateText.Text = "Audioquellen werden gelesen …";
            IReadOnlyList<ObsInputInfo> inputs = await _obsClient.GetInputListAsync();
            var entries = new List<ObsAudioProfileEntrySettings>();
            foreach (ObsInputInfo input in inputs)
            {
                try
                {
                    ObsInputAudioState state = await _obsClient.GetInputAudioStateAsync(input.Name);
                    ObsInputAdvancedAudioState advanced = await _obsClient.GetInputAdvancedAudioStateAsync(input.Name);
                    entries.Add(new ObsAudioProfileEntrySettings
                    {
                        InputName = input.Name,
                        VolumeDb = state.VolumeDb,
                        Muted = state.Muted,
                        MonitorType = advanced.MonitorType,
                        SyncOffsetMilliseconds = advanced.SyncOffsetMilliseconds
                    });
                }
                catch
                {
                    // Nicht jede OBS-Quelle besitzt Audioeigenschaften.
                }
            }
            if (entries.Count == 0)
            {
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileStateText.Text = "Keine steuerbaren Audioquellen gefunden.";
                AddDashboardNotification("OBS meldet keine steuerbaren Audioquellen für das Profil.", "Warnung");
                return;
            }
            _settings.Obs.AudioProfiles.RemoveAll(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
            _settings.Obs.AudioProfiles.Add(new ObsAudioProfileSettings { Name = name, Inputs = entries });
            await _settingsStore.SaveAsync(_settings);
            RefreshObsAudioProfilesUi(name);
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileStateText.Text = $"Profil „{name}“ mit {entries.Count} Audioquellen gespeichert.";
            AddDashboardNotification($"OBS-Audio-Profil „{name}“ gespeichert.", "Info");
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileStateText.Text = "Profil konnte nicht gespeichert werden: " + exception.Message;
            AddDashboardNotification("OBS-Audio-Profil konnte nicht gespeichert werden: " + exception.Message, "Fehler");
        }
    }

    private async Task ApplySelectedObsAudioProfileAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.SelectedItem is not ObsAudioProfileSettings profile)
        {
            AddDashboardNotification("Bitte OBS verbinden und ein Audio-Profil auswählen.", "Warnung");
            return;
        }
        int applied = 0;
        var missing = new List<string>();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyAudioProfileButton.IsEnabled = false;
        try
        {
            foreach (ObsAudioProfileEntrySettings entry in profile.Inputs)
            {
                try
                {
                    if (!await _obsClient.InputExistsAsync(entry.InputName))
                    {
                        missing.Add(entry.InputName);
                        continue;
                    }
                    await _obsClient.SetInputVolumeDbAsync(entry.InputName, Math.Clamp(entry.VolumeDb, -100, 26));
                    await _obsClient.SetInputMuteAsync(entry.InputName, entry.Muted);
                    await _obsClient.SetInputAudioMonitorTypeAsync(entry.InputName, entry.MonitorType);
                    await _obsClient.SetInputAudioSyncOffsetAsync(entry.InputName, Math.Clamp(entry.SyncOffsetMilliseconds, -950, 20000));
                    applied++;
                }
                catch
                {
                    missing.Add(entry.InputName);
                }
            }
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileStateText.Text = missing.Count == 0
                ? $"Profil „{profile.Name}“ vollständig angewendet ({applied} Quellen)."
                : $"Profil angewendet: {applied} erfolgreich, {missing.Count} nicht verfügbar.";
            AddDashboardNotification($"OBS-Audio-Profil „{profile.Name}“ angewendet: {applied} Quellen.", missing.Count == 0 ? "Info" : "Warnung");
            await RefreshSelectedObsInputStateAsync();
        }
        finally
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyAudioProfileButton.IsEnabled = true;
        }
    }

    private async Task DeleteSelectedObsAudioProfileAsync()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.SelectedItem is not ObsAudioProfileSettings profile)
        {
            return;
        }

        _settings.Obs.AudioProfiles.RemoveAll(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        await _settingsStore.SaveAsync(_settings);
        RefreshObsAudioProfilesUi();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileNameBox.Clear();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileStateText.Text = $"Profil „{profile.Name}“ gelöscht.";
        AddDashboardNotification($"OBS-Audio-Profil „{profile.Name}“ gelöscht.", "Info");
    }
}
