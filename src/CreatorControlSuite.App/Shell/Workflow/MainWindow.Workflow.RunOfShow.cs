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

public partial class MainWindow
{
    private RunOfShowPlanSettings EnsureRunOfShowPlansInitialized()
    {
        _settings.Obs.AudioProfiles ??= [];
        RefreshObsAudioProfilesUi();
        return RunOfShowPlanService.EnsureInitialized(_settings.Workflow);
    }

    private RunOfShowPlanSettings? CurrentRunOfShowPlan()
        => _settings.Workflow.RunOfShowPlans.FirstOrDefault(x =>
            string.Equals(x.Id, _settings.Workflow.ActiveRunOfShowPlanId, StringComparison.OrdinalIgnoreCase));

    private void RefreshRunOfShowPlanSelector()
    {
        RunOfShowPlanSettings active = EnsureRunOfShowPlansInitialized();
        _updatingRunOfShowPlanUi = true;
        try
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.ItemsSource = null;
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.ItemsSource = _settings.Workflow.RunOfShowPlans;
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.SelectedItem = active;
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.Text = active.Name;
            WorkflowPageViewHost.RunOfShowViewHost.DeleteRunOfShowPlanButton.IsEnabled = _settings.Workflow.RunOfShowPlans.Count > 1;
        }
        finally
        {
            _updatingRunOfShowPlanUi = false;
        }
    }

    private void RefreshRunOfShowSteps()
    {
        RunOfShowPlanSettings active = EnsureRunOfShowPlansInitialized();
        RefreshRunOfShowPlanSelector();
        _runOfShowSteps.Clear();
        foreach (RunOfShowStepSettings step in active.Steps)
        {
            _runOfShowSteps.Add(step);
        }

        if (_runOfShowSteps.Count > 0 && WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is null)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedIndex = 0;
        }

        _runOfShowCurrentIndex = -1;
        UpdateRunOfShowStatus();
    }

    private async Task PersistRunOfShowAsync()
    {
        RunOfShowPlanSettings active = EnsureRunOfShowPlansInitialized();
        active.Steps = [.. _runOfShowSteps];
        _settings.Workflow.RunOfShowSteps = active.Steps;
        await _settingsStore.SaveAsync(_settings);
    }

    private async Task SwitchRunOfShowPlanAsync()
    {
        if (_updatingRunOfShowPlanUi || WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.SelectedItem is not RunOfShowPlanSettings selected)
        {
            return;
        }

        StopAutomaticRunOfShow();
        if (CurrentRunOfShowPlan() is not null)
        {
            await PersistRunOfShowAsync();
        }

        RunOfShowPlanService.ActivatePlan(_settings.Workflow, selected);
        _runOfShowSteps.Clear();
        foreach (RunOfShowStepSettings step in _settings.Workflow.RunOfShowSteps)
        {
            _runOfShowSteps.Add(step);
        }

        _runOfShowCurrentIndex = -1;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedIndex = _runOfShowSteps.Count > 0 ? 0 : -1;
        await _settingsStore.SaveAsync(_settings);
        UpdateRunOfShowStatus();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"Regieplan '{selected.Name}' geladen.";
    }

    private async Task CreateRunOfShowPlanAsync()
    {
        await PersistRunOfShowAsync();
        RunOfShowPlanSettings plan =
            RunOfShowPlanService.CreateAndActivatePlan(_settings.Workflow);
        _runOfShowSteps.Clear();
        _runOfShowCurrentIndex = -1;
        RefreshRunOfShowPlanSelector();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.SelectedItem = plan;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.Text = plan.Name;
        await _settingsStore.SaveAsync(_settings);
        UpdateRunOfShowStatus();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"Regieplan '{plan.Name}' erstellt. Namen im Feld ändern und UMBENENNEN wählen.";
    }

    private async Task RenameRunOfShowPlanAsync()
    {
        RunOfShowPlanSettings? plan = CurrentRunOfShowPlan();
        if (plan is null)
        {
            return;
        }

        string? error = RunOfShowPlanService.RenamePlan(
            plan,
            _settings.Workflow.RunOfShowPlans,
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.Text);
        if (error is not null)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = error;
            return;
        }

        RefreshRunOfShowPlanSelector();
        await _settingsStore.SaveAsync(_settings);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"Regieplan in '{plan.Name}' umbenannt.";
    }

    private async Task DeleteRunOfShowPlanAsync()
    {
        RunOfShowPlanSettings? plan = CurrentRunOfShowPlan();
        if (plan is null || _settings.Workflow.RunOfShowPlans.Count <= 1)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Der letzte Regieplan kann nicht gelöscht werden.";
            return;
        }
        MessageBoxResult answer = MessageBox.Show(this, $"Regieplan '{plan.Name}' einschließlich aller Schritte löschen?",
            "Regieplan löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        StopAutomaticRunOfShow();
        RunOfShowPlanSettings next =
            RunOfShowPlanService.DeletePlanAndActivateNext(
                _settings.Workflow,
                plan);
        _runOfShowSteps.Clear();
        foreach (RunOfShowStepSettings step in next.Steps)
        {
            _runOfShowSteps.Add(step);
        }

        _runOfShowCurrentIndex = -1;
        RefreshRunOfShowPlanSelector();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedIndex = _runOfShowSteps.Count > 0 ? 0 : -1;
        await _settingsStore.SaveAsync(_settings);
        UpdateRunOfShowStatus();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"Regieplan '{plan.Name}' gelöscht. '{next.Name}' ist jetzt aktiv.";
    }

    private void CreateNewRunOfShowStep()
    {
        var step = new RunOfShowStepSettings();
        _settings.Workflow.RunOfShowSteps.Add(step);
        _runOfShowSteps.Add(step);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem = step;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.ScrollIntoView(step);
    }

    private async Task DuplicateSelectedRunOfShowStepAsync()
    {
        if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings source)
        {
            return;
        }

        RunOfShowStepSettings copy =
            RunOfShowPlanService.CloneStep(source);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = source.Name + " (Kopie)";
        int index = _runOfShowSteps.IndexOf(source) + 1;
        _settings.Workflow.RunOfShowSteps.Insert(index, copy);
        _runOfShowSteps.Insert(index, copy);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem = copy;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.ScrollIntoView(copy);
        await PersistRunOfShowAsync();
        UpdateRunOfShowStatus();
    }

    private async Task MoveSelectedRunOfShowStepAsync(int direction)
    {
        if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step)
        {
            return;
        }

        int oldIndex = _runOfShowSteps.IndexOf(step);
        int newIndex = oldIndex + direction;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= _runOfShowSteps.Count)
        {
            return;
        }

        _runOfShowSteps.Move(oldIndex, newIndex);
        _settings.Workflow.RunOfShowSteps.Remove(step);
        _settings.Workflow.RunOfShowSteps.Insert(newIndex, step);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem = step;
        if (_runOfShowCurrentIndex == oldIndex)
        {
            _runOfShowCurrentIndex = newIndex;
        }
        else if (_runOfShowCurrentIndex == newIndex)
        {
            _runOfShowCurrentIndex = oldIndex;
        }

        await PersistRunOfShowAsync();
        UpdateRunOfShowStatus();
    }

    private void LoadSelectedRunOfShowStep()
    {
        if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step)
        {
            return;
        }

        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowEnabledBox.IsChecked = step.Enabled;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowNameBox.Text = step.Name;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSceneBox.Text = step.ObsScene;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionBox.Text = step.TransitionName;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionDurationBox.Text = step.TransitionDurationMilliseconds.ToString();
        SelectComboByTag(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSpotifyActionBox, step.SpotifyAction);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSpotifyVolumeBox.Text = step.SpotifyVolumePercent.ToString();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowActionDelayBox.Text = step.ActionDelayMilliseconds.ToString();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowContinueOnActionErrorBox.IsChecked = step.ContinueOnActionError;
        SelectStreamerBotAction(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStreamerBotActionBox, step.StreamerBotActionId, step.StreamerBotActionName);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowUpdateTwitchBox.IsChecked = step.UpdateTwitchChannel;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchTitleBox.Text = step.TwitchTitle;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategorySearchBox.Text = step.TwitchCategoryName;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategoryResultsBox.ItemsSource = string.IsNullOrWhiteSpace(step.TwitchCategoryId)
            ? null
            : new[] { new TwitchCategory(step.TwitchCategoryId, step.TwitchCategoryName, "") };
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategoryResultsBox.SelectedIndex = string.IsNullOrWhiteSpace(step.TwitchCategoryId) ? -1 : 0;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowContinueOnTwitchErrorBox.IsChecked = step.ContinueOnTwitchError;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowAutoAdvanceBox.IsChecked = step.AutoAdvance;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowAutoAdvanceDelayBox.Text = step.AutoAdvanceDelaySeconds.ToString();
    }

    private RunOfShowStepSettings ReadRunOfShowEditor(RunOfShowStepSettings? target = null)
    {
        RunOfShowStepSettings step = target ?? new RunOfShowStepSettings();
        step.Enabled = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowEnabledBox.IsChecked == true;
        step.Name = string.IsNullOrWhiteSpace(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowNameBox.Text) ? "Neuer Regieschritt" : WorkflowPageViewHost.RunOfShowViewHost.RunOfShowNameBox.Text.Trim();
        step.ObsScene = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSceneBox.Text.Trim();
        step.TransitionName = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionBox.Text.Trim();
        step.TransitionDurationMilliseconds = int.TryParse(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionDurationBox.Text, out int duration) ? Math.Clamp(duration, 50, 20000) : 1000;
        step.SpotifyAction = ComboTag(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSpotifyActionBox, "None");
        step.SpotifyVolumePercent = int.TryParse(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSpotifyVolumeBox.Text, out int volume) ? Math.Clamp(volume, 0, 100) : 35;
        var streamerAction = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStreamerBotActionBox.SelectedItem as StreamerBotActionOption;
        step.StreamerBotActionId = streamerAction?.Id ?? "";
        step.StreamerBotActionName = streamerAction?.Name ?? WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStreamerBotActionBox.Text.Trim();
        step.ActionDelayMilliseconds = int.TryParse(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowActionDelayBox.Text, out int actionDelay) ? Math.Clamp(actionDelay, 0, 60000) : 0;
        step.ContinueOnActionError = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowContinueOnActionErrorBox.IsChecked == true;
        step.UpdateTwitchChannel = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowUpdateTwitchBox.IsChecked == true;
        step.TwitchTitle = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchTitleBox.Text.Trim();
        var twitchCategory = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategoryResultsBox.SelectedItem as TwitchCategory;
        step.TwitchCategoryId = twitchCategory?.Id ?? step.TwitchCategoryId;
        step.TwitchCategoryName = twitchCategory?.Name ?? WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategorySearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(step.TwitchCategoryName))
        {
            step.TwitchCategoryId = "";
        }

        step.ContinueOnTwitchError = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowContinueOnTwitchErrorBox.IsChecked == true;
        step.AutoAdvance = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowAutoAdvanceBox.IsChecked == true;
        step.AutoAdvanceDelaySeconds = int.TryParse(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowAutoAdvanceDelayBox.Text, out int autoDelay) ? Math.Clamp(autoDelay, 1, 86400) : 10;
        return step;
    }

    private async Task SaveSelectedRunOfShowStepAsync()
    {
        var step = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem as RunOfShowStepSettings;
        if (step is null)
        {
            CreateNewRunOfShowStep();
            step = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem as RunOfShowStepSettings;
        }
        if (step is null)
        {
            return;
        }

        ReadRunOfShowEditor(step);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.Items.Refresh();
        await PersistRunOfShowAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Regieschritt gespeichert.";
    }

    private async Task DeleteSelectedRunOfShowStepAsync()
    {
        if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step)
        {
            return;
        }

        int index = _runOfShowSteps.IndexOf(step);
        _settings.Workflow.RunOfShowSteps.Remove(step);
        _runOfShowSteps.Remove(step);
        if (_runOfShowCurrentIndex >= _runOfShowSteps.Count)
        {
            _runOfShowCurrentIndex = _runOfShowSteps.Count - 1;
        }

        if (_runOfShowSteps.Count > 0)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedIndex = Math.Clamp(index, 0, _runOfShowSteps.Count - 1);
        }

        await PersistRunOfShowAsync();
        UpdateRunOfShowStatus();
    }

    private async Task RefreshRunOfShowObsListsAsync()
    {
        if (!_obsClient.IsConnected)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }
        string previousScene = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSceneBox.Text;
        string previousTransition = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionBox.Text;
        var scenes = (await _obsClient.GetSceneListAsync()).Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var transitions = (await _obsClient.GetSceneTransitionListAsync()).Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSceneBox.ItemsSource = scenes;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionBox.ItemsSource = transitions;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSceneBox.Text = previousScene;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionBox.Text = previousTransition;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"{scenes.Count} Szenen und {transitions.Count} Übergänge geladen.";
    }

    private async Task RefreshRunOfShowStreamerBotActionsAsync(bool showStatus)
    {
        await RefreshStreamerBotActionsAsync(false);
        if (!showStatus)
        {
            return;
        }

        if (!_streamerBotClient.IsConnected)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Streamer.bot ist nicht verbunden.";
            return;
        }
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"{_streamerBotActions.Count} Streamer.bot-Aktionen für den Regieplan geladen.";
    }

    private async Task SearchRunOfShowTwitchCategoriesAsync()
    {
        try
        {
            string query = WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategorySearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Bitte einen Kategorienamen eingeben.";
                return;
            }

            IReadOnlyList<TwitchCategory> categories = await _twitchModule.SearchCategoriesAsync(query);
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategoryResultsBox.ItemsSource = categories;
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategoryResultsBox.SelectedIndex = categories.Count > 0 ? 0 : -1;
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = categories.Count > 0
                ? $"{categories.Count} Twitch-Kategorien gefunden."
                : "Keine passende Twitch-Kategorie gefunden.";
        }
        catch (Exception ex)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Twitch-Kategoriesuche fehlgeschlagen: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Twitch", WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
    }


    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(value) ? "Mein-Regieplan.ccs-regieplan.json" : value;
    }

    private sealed class RunOfShowExportDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string Name { get; set; } = "CastingCouch Regieplan";
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
        public List<RunOfShowStepSettings> Steps { get; set; } = [];
    }

    private async Task ExportRunOfShowAsync()
    {
        try
        {
            if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is RunOfShowStepSettings selected)
            {
                ReadRunOfShowEditor(selected);
            }

            if (_runOfShowSteps.Count == 0)
            {
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Der Regieplan enthält noch keine Schritte.";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Regieplan exportieren",
                Filter = "CastingCouch Regieplan (*.ccs-regieplan.json)|*.ccs-regieplan.json|JSON-Datei (*.json)|*.json",
                DefaultExt = ".ccs-regieplan.json",
                AddExtension = true,
                FileName = SanitizeFileName((CurrentRunOfShowPlan()?.Name ?? "Mein-Regieplan") + ".ccs-regieplan.json")
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var document = new RunOfShowExportDocument
            {
                Name = CurrentRunOfShowPlan()?.Name ?? "CastingCouch Regieplan",
                Steps = [.. _runOfShowSteps.Select(
                    RunOfShowPlanService.CloneStep)]
            };
            string json = System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dialog.FileName, json);
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"Regieplan exportiert: {Path.GetFileName(dialog.FileName)}";
            _appLogger.Write(AppLogLevel.Information, "RunOfShow.Export", WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
        catch (Exception ex)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Regieplan konnte nicht exportiert werden: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Export", WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
    }

    private async Task ImportRunOfShowAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Regieplan importieren",
                Filter = "CastingCouch Regieplan (*.ccs-regieplan.json;*.json)|*.ccs-regieplan.json;*.json|Alle Dateien (*.*)|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string json = await File.ReadAllTextAsync(dialog.FileName);
            RunOfShowExportDocument? document = System.Text.Json.JsonSerializer.Deserialize<RunOfShowExportDocument>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (document?.Steps is null || document.Steps.Count == 0)
            {
                throw new InvalidDataException("Die Datei enthält keine Regieschritte.");
            }

            if (document.FormatVersion < 1 || document.FormatVersion > 1)
            {
                throw new InvalidDataException($"Nicht unterstützte Regieplan-Version: {document.FormatVersion}.");
            }

            MessageBoxResult answer = MessageBox.Show(this,
                $"{document.Steps.Count} Regieschritte wurden gefunden. Soll der aktuelle Regieplan ersetzt werden?{Environment.NewLine}{Environment.NewLine}Ja = ersetzen{Environment.NewLine}Nein = anhängen",
                "Regieplan importieren", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            StopAutomaticRunOfShow();
            if (answer == MessageBoxResult.Yes)
            {
                _runOfShowSteps.Clear();
            }

            foreach (RunOfShowStepSettings imported in document.Steps)
            {
                RunOfShowStepSettings step =
                    RunOfShowPlanService.PrepareImportedStep(imported);
                _runOfShowSteps.Add(step);
            }

            _runOfShowCurrentIndex = -1;
            await PersistRunOfShowAsync();
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.ItemsSource = null;
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.ItemsSource = _runOfShowSteps;
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedIndex = _runOfShowSteps.Count > 0 ? 0 : -1;
            UpdateRunOfShowStatus();
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"{document.Steps.Count} Regieschritte importiert.";
            _appLogger.Write(AppLogLevel.Information, "RunOfShow.Import", WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
        catch (Exception ex)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Regieplan konnte nicht importiert werden: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Import", WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
    }

    private async Task ValidateRunOfShowAsync()
    {
        try
        {
            if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is RunOfShowStepSettings selected)
            {
                ReadRunOfShowEditor(selected);
            }

            if (_runOfShowSteps.Count == 0)
            {
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Der Regieplan enthält noch keine Schritte.";
                return;
            }

            IReadOnlyList<ObsSceneInfo> obsScenes = _obsClient.IsConnected
                ? await _obsClient.GetSceneListAsync()
                : [];
            IReadOnlyList<string> issues = RunOfShowPlanService.Validate(
                _runOfShowSteps,
                obsScenes.Select(scene => scene.Name),
                _obsClient.IsConnected);

            if (issues.Count == 0)
            {
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"Regieplan geprüft: {_runOfShowSteps.Count} Schritte, keine Fehler gefunden.";
                MessageBox.Show(this, WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text, "Regieplanprüfung", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = $"Regieplanprüfung: {issues.Count} Hinweis(e) gefunden.";
                MessageBox.Show(this, string.Join(Environment.NewLine, issues.Take(25)) + (issues.Count > 25 ? $"{Environment.NewLine}... und {issues.Count - 25} weitere." : string.Empty), "Regieplanprüfung", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _appLogger.Write(issues.Count == 0 ? AppLogLevel.Information : AppLogLevel.Warning, "RunOfShow.Validation", WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
        catch (Exception ex)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text = "Regieplanprüfung fehlgeschlagen: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Validation", WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
    }

}
