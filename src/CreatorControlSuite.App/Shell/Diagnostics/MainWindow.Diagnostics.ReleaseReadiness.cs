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
    private async Task CreateSupportPackageAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CastingCouch Supportpaket (*.ccssupport)|*.ccssupport",
            FileName = "CreatorControlSuite-Support-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ccssupport"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SupportPackageResult result = await _supportPackageService.CreateAsync(dialog.FileName, new SupportPackageOptions(true, true, true, true, true, true));
            MessageBox.Show("Supportpaket erstellt:\n\n" + result.PackagePath + (result.Warnings.Count == 0 ? "" : "\n\nHinweise:\n" + string.Join("\n", result.Warnings.Select(x => "• " + x))), "CastingCouch", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Error, "Support", "Supportpaket konnte nicht erstellt werden.", exception);
            MessageBox.Show(exception.Message, "Supportpaket", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RunReleaseCheckAsync()
    {
        ReleaseReadinessReport report = await _releaseReadinessService.CheckAsync();
        ReleaseReadinessGrid.ItemsSource = report.Items;
        MessageBox.Show(report.Ready ? "Der technische Release-Check ist bestanden." : "Der Release-Check enthält blockierende Punkte.", "Release-Check", MessageBoxButton.OK, report.Ready ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task RunInstallerSelfTestAsync()
    {
        try
        {
            InstallerSelfTestReport report = await _installerSelfTestService.RunAsync();
            InstallerSelfTestGrid.ItemsSource = report.Items;
            MessageBox.Show(report.Passed ? "Installer-Selbsttest bestanden." : "Installer-Selbsttest enthält Fehler.",
                "Installer-Selbsttest", MessageBoxButton.OK, report.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _appLogger.Write(AppLogLevel.Error, "InstallerSelfTest", "Installer-Selbsttest ist fehlgeschlagen.", ex);
            MessageBox.Show(ex.Message, "Installer-Selbsttest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshBetaReadinessAsync()
    {
        try
        {
            BetaReadinessDashboard d = await _betaReadinessService.BuildAsync();
            BetaReadinessGrid.ItemsSource = d.Areas; BetaReadinessScoreText.Text = d.OverallScorePercent + " %";
            BetaReadinessStatusText.Text = d.BetaReady ? "Beta technisch bereit" : "Noch nicht Beta-bereit";
            BetaReadinessStatusText.Foreground = d.BetaReady ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed;
            BetaBlockersTextBox.Text = d.Blockers.Count == 0 ? "Keine technischen Blocker erkannt." :
                string.Join(Environment.NewLine, d.Blockers.Select(x => "• " + x));
        }
        catch (Exception ex)
        {
            _appLogger.Write(AppLogLevel.Error, "BetaReadiness", "Beta-Readiness konnte nicht ermittelt werden.", ex);
            MessageBox.Show(ex.Message, "Beta-Readiness", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RunWorkflowE2eAsync()
    {
        if (MessageBox.Show("Der Test führt den echten Workflow Vorbereiten → Live → Pause → Fortsetzen → Ende aus. OBS und konfigurierte Dienste können gesteuert werden. Jetzt starten?", "Workflow E2E-Test", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            WorkflowE2eReport report = await _workflowE2eService.RunAsync();
            WorkflowE2eGrid.ItemsSource = report.Steps;
            MessageBox.Show(report.Success ? "Workflow E2E-Test erfolgreich." : "Workflow E2E-Test enthält Fehler.", "Workflow E2E-Test");
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Error, "E2E", "Workflow E2E-Test fehlgeschlagen.", exception);
            MessageBox.Show(exception.Message, "Workflow E2E-Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshProfilesAsync()
    {
        await _profilesPageViewModel.RefreshAsync();
        DashboardPageViewHost.DashboardProfileBox.ItemsSource = _profilesPageViewModel.Profiles;
        if (DashboardPageViewHost.DashboardProfileBox.SelectedItem is null && _profilesPageViewModel.Profiles.Count > 0)
        {
            DashboardPageViewHost.DashboardProfileBox.SelectedIndex = 0;
        }
    }





    private void OpenLocalDataFolder(string child)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            child);

        Directory.CreateDirectory(path);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
    }

    private static string GetCurrentProductVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        string? informationalVersion = assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            int metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
