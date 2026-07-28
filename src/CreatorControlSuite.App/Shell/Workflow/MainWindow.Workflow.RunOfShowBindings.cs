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
    private void InitializeRunOfShowBindings()
    {
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.ItemsSource = _runOfShowSteps;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowPlanBox.SelectionChanged += async (_, _) => await SwitchRunOfShowPlanAsync();
        WorkflowPageViewHost.RunOfShowViewHost.NewRunOfShowPlanButton.Click += async (_, _) => await CreateRunOfShowPlanAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RenameRunOfShowPlanButton.Click += async (_, _) => await RenameRunOfShowPlanAsync();
        WorkflowPageViewHost.RunOfShowViewHost.DeleteRunOfShowPlanButton.Click += async (_, _) => await DeleteRunOfShowPlanAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectionChanged += (_, _) => LoadSelectedRunOfShowStep();
        WorkflowPageViewHost.RunOfShowViewHost.NewRunOfShowStepButton.Click += (_, _) => CreateNewRunOfShowStep();
        WorkflowPageViewHost.RunOfShowViewHost.DuplicateRunOfShowStepButton.Click += async (_, _) => await DuplicateSelectedRunOfShowStepAsync();
        WorkflowPageViewHost.RunOfShowViewHost.MoveRunOfShowStepUpButton.Click += async (_, _) => await MoveSelectedRunOfShowStepAsync(-1);
        WorkflowPageViewHost.RunOfShowViewHost.MoveRunOfShowStepDownButton.Click += async (_, _) => await MoveSelectedRunOfShowStepAsync(1);
        WorkflowPageViewHost.RunOfShowViewHost.DeleteRunOfShowStepButton.Click += async (_, _) => await DeleteSelectedRunOfShowStepAsync();
        WorkflowPageViewHost.RunOfShowViewHost.ImportRunOfShowButton.Click += async (_, _) => await ImportRunOfShowAsync();
        WorkflowPageViewHost.RunOfShowViewHost.ExportRunOfShowButton.Click += async (_, _) => await ExportRunOfShowAsync();
        WorkflowPageViewHost.RunOfShowViewHost.ValidateRunOfShowButton.Click += async (_, _) => await ValidateRunOfShowAsync();
        WorkflowPageViewHost.RunOfShowViewHost.SaveRunOfShowStepButton.Click += async (_, _) => await SaveSelectedRunOfShowStepAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RefreshRunOfShowObsButton.Click += async (_, _) => await RefreshRunOfShowObsListsAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowSceneBox.DropDownOpened += async (_, _) => await RefreshRunOfShowObsListsAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTransitionBox.DropDownOpened += async (_, _) => await RefreshRunOfShowObsListsAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStreamerBotActionBox.DropDownOpened += async (_, _) => await RefreshRunOfShowStreamerBotActionsAsync(false);
        WorkflowPageViewHost.RunOfShowViewHost.RefreshRunOfShowStreamerBotActionsButton.Click += async (_, _) => await RefreshRunOfShowStreamerBotActionsAsync(true);
        WorkflowPageViewHost.RunOfShowViewHost.SearchRunOfShowTwitchCategoryButton.Click += async (_, _) => await SearchRunOfShowTwitchCategoriesAsync();
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowTwitchCategorySearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await SearchRunOfShowTwitchCategoriesAsync();
            }
        };
        WorkflowPageViewHost.RunOfShowViewHost.ExecuteRunOfShowStepButton.Click += async (_, _) => await ExecuteSelectedRunOfShowStepAsync();
        WorkflowPageViewHost.RunOfShowViewHost.ExecuteNextRunOfShowStepButton.Click += async (_, _) => await ExecuteNextRunOfShowStepAsync();
        WorkflowPageViewHost.RunOfShowViewHost.ResetRunOfShowButton.Click += (_, _) => ResetRunOfShow();
        WorkflowPageViewHost.RunOfShowViewHost.StartAutomaticRunOfShowButton.Click += async (_, _) => await StartAutomaticRunOfShowAsync();
        WorkflowPageViewHost.RunOfShowViewHost.StopAutomaticRunOfShowButton.Click += (_, _) => StopAutomaticRunOfShow();
    }
}
