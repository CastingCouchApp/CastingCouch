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
    private void InitializeStreamDeckBindings()
    {
        ServicesPageViewHost.StreamDeckServiceViewHost.CreateStreamDeckActionButton.Click += async (_, _) => await CreateStreamDeckActionAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.OpenStreamDeckActionsFolderButton.Click += (_, _) => OpenStreamDeckActionsFolder();
        ServicesPageViewHost.StreamDeckServiceViewHost.RefreshStreamDeckActionsButton.Click += (_, _) => RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost.DeleteStreamDeckActionButton.Click += (_, _) => DeleteSelectedStreamDeckAction();
        ServicesPageViewHost.StreamDeckServiceViewHost.TestStreamDeckActionButton.Click += async (_, _) => await TestSelectedStreamDeckActionAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.DuplicateStreamDeckActionButton.Click += async (_, _) => await DuplicateSelectedStreamDeckActionAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.DuplicateStreamDeckProfileButton.Click += async (_, _) => await DuplicateSelectedStreamDeckProfileAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.ResolveStreamDeckConflictsButton.Click += async (_, _) => await ResolveStreamDeckConflictsAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.ActivateStreamDeckViewButton.Click += (_, _) => ActivateSelectedStreamDeckView();
        ServicesPageViewHost.StreamDeckServiceViewHost.LockStreamDeckActionButton.Click += async (_, _) => await ToggleSelectedStreamDeckActionLockAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.BackupStreamDeckConfigurationButton.Click += (_, _) => BackupStreamDeckConfiguration();
        ServicesPageViewHost.StreamDeckServiceViewHost.RestoreStreamDeckConfigurationButton.Click += (_, _) => RestoreStreamDeckConfiguration();
        ServicesPageViewHost.StreamDeckServiceViewHost.ExportStreamDeckActionsButton.Click += (_, _) => ExportStreamDeckActionCatalog();
        ServicesPageViewHost.StreamDeckServiceViewHost.ImportStreamDeckActionsButton.Click += (_, _) => ImportStreamDeckActionCatalog();
        ServicesPageViewHost.StreamDeckServiceViewHost.ExportSingleStreamDeckActionButton.Click += (_, _) => ExportSelectedStreamDeckAction();
        ServicesPageViewHost.StreamDeckServiceViewHost.ImportSingleStreamDeckActionButton.Click += (_, _) => ImportSingleStreamDeckAction();
        ServicesPageViewHost.StreamDeckServiceViewHost.QuickAssignStreamDeckActionButton.Click += async (_, _) => await QuickAssignSelectedStreamDeckActionAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.CompareStreamDeckProfilesButton.Click += (_, _) => CompareStreamDeckProfiles();
        ServicesPageViewHost.StreamDeckServiceViewHost.SaveStreamDeckTemplateButton.Click += async (_, _) => await SaveStreamDeckTemplateAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.LoadStreamDeckTemplateButton.Click += async (_, _) => await LoadSelectedStreamDeckTemplateAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.DeleteStreamDeckTemplateButton.Click += (_, _) => DeleteSelectedStreamDeckTemplate();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectionChanged += (_, _) => RefreshSelectedStreamDeckActionDetails();
        ServicesPageViewHost.StreamDeckServiceViewHost.ApplyStreamDeckFilterButton.Click += (_, _) => RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost.SyncStreamDeckStateButton.Click += async (_, _) => await SyncStreamDeckRuntimeStateAsync(true);
        ServicesPageViewHost.StreamDeckServiceViewHost.DiagnoseStreamDeckActionsButton.Click += (_, _) => DiagnoseStreamDeckActions();
        ServicesPageViewHost.StreamDeckServiceViewHost.SimulateStreamDeckActionButton.Click += async (_, _) => await SimulateSelectedStreamDeckActionAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.ClearStreamDeckExecutionLogButton.Click += (_, _) => ClearStreamDeckExecutionLog();
        ServicesPageViewHost.StreamDeckServiceViewHost.AddStreamDeckRuleButton.Click += async (_, _) => await AddStreamDeckAutomationRuleAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.DeleteStreamDeckRuleButton.Click += (_, _) => DeleteSelectedStreamDeckAutomationRule();
        ServicesPageViewHost.StreamDeckServiceViewHost.EvaluateStreamDeckRulesButton.Click += async (_, _) => await EvaluateStreamDeckAutomationRulesAsync(true);
        ServicesPageViewHost.StreamDeckServiceViewHost.PreviewStreamDeckRulesButton.Click += async (_, _) => await EvaluateStreamDeckAutomationRulesAsync(true, true);
        ServicesPageViewHost.StreamDeckServiceViewHost.TestStreamDeckRulesButton.Click += (_, _) => TestStreamDeckAutomationRules();
        ServicesPageViewHost.StreamDeckServiceViewHost.ClearStreamDeckRuleHistoryButton.Click += (_, _) => { _streamDeckRuleHistory.Clear(); ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleHistoryBox.Clear(); ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Entscheidungsverlauf geleert."; };
        ServicesPageViewHost.StreamDeckServiceViewHost.SaveStreamDeckRuleTemplateButton.Click += async (_, _) => await SaveSelectedStreamDeckRuleTemplateAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.LoadStreamDeckRuleTemplateButton.Click += async (_, _) => await LoadStreamDeckRuleTemplateAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.ExportStreamDeckRuleSetButton.Click += (_, _) => ExportStreamDeckRuleSet();
        ServicesPageViewHost.StreamDeckServiceViewHost.ImportStreamDeckRuleSetButton.Click += async (_, _) => await ImportStreamDeckRuleSetAsync();
        ServicesPageViewHost.StreamDeckServiceViewHost.AnalyzeStreamDeckRuleConflictsButton.Click += (_, _) => AnalyzeStreamDeckRuleConflicts();
        ServicesPageViewHost.StreamDeckServiceViewHost.RestoreStableStreamDeckStateButton.Click += (_, _) => RestoreStableStreamDeckState();
        ServicesPageViewHost.StreamDeckServiceViewHost.ShowStreamDeckRuleStatisticsButton.Click += (_, _) => ShowStreamDeckRuleStatistics();
        ServicesPageViewHost.StreamDeckServiceViewHost.ExportStreamDeckRuleDiagnosticsButton.Click += (_, _) => ExportStreamDeckRuleDiagnostics();
        ServicesPageViewHost.StreamDeckServiceViewHost.ResetStreamDeckRuleStatisticsButton.Click += async (_, _) => await ResetStreamDeckRuleStatisticsAsync();
        _streamDeckStateSyncTimer.Tick += async (_, _) =>
        {
            if (ServicesPageViewHost.StreamDeckServiceViewHost.AutoSyncStreamDeckStateBox.IsChecked == true)
            {
                await SyncStreamDeckRuntimeStateAsync(false);
            }
        };
        _streamDeckStateSyncTimer.Start();
        _streamDeckRuleTimer.Tick += async (_, _) => await EvaluateStreamDeckAutomationRulesAsync(false);
        _streamDeckRuleTimer.Start();
        RefreshStreamDeckActionsList();
        RefreshStreamDeckTemplates();
        RefreshStreamDeckExecutionLog();
        RefreshStreamDeckAutomationRules();
        SettingsPageViewHost.InstallStreamDeckButton.Click += async (_, _) =>
            await ExportStreamDeckProfileAsync();

        SettingsPageViewHost.OpenStreamDeckFolderButton.Click += (_, _) =>
            OpenLocalDataFolder("StreamDeck");
    }
}
