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
    private async Task RefreshCreatorIntelligenceAsync()
    {
        CreatorIntelligenceSummary? summary = await _creatorIntelligence.AnalyzeLatestSessionAsync();
        if (summary is null)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = _creatorIntelligence.IsRecording
                ? "Session-Aufzeichnung aktiv · erste Auswertung nach Streamende."
                : "Noch keine Creator-Intelligence-Session vorhanden.";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceScoreText.Text = "–";
            _creatorIntelligenceRecommendations.Clear();
            _creatorIntelligenceRecommendations.Add("Starte einen Stream, damit Twitch-, OBS- und Ereignisdaten gemeinsam aufgezeichnet werden.");
            ApplyCreatorIntelligenceDashboard(await _creatorIntelligence.AnalyzeDashboardAsync(30));
            ApplyCreatorContentPerformance(await _creatorIntelligence.AnalyzeContentPerformanceAsync(30));
            ApplyCreatorEventCorrelations(await _creatorIntelligence.AnalyzeEventCorrelationsAsync(30));
            ApplyCreatorActionPlan(await _creatorIntelligence.AnalyzeActionPlanAsync());
            ApplyCreatorActionEffectiveness(await _creatorIntelligence.AnalyzeActionEffectivenessAsync());
            return;
        }

        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceScoreText.Text = summary.CreatorScore.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = $"Letzte Session: {summary.StartedAt:dd.MM.yyyy HH:mm} · Ø {summary.AverageViewers:0.0} · Peak {summary.PeakViewers}";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRetentionText.Text = $"{summary.RetentionPercent:0}%";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEngagementText.Text = $"{summary.ChatMessagesPerHour:0.0}/h";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceGrowthText.Text = $"{summary.FollowersPerHour:0.0}/h";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceContextText.Text = $"{summary.DistinctScenes} Szenen · {summary.TracksPlayed} Songs · {summary.ChatMessages} Chatnachrichten";
        _creatorIntelligenceRecommendations.Clear();
        foreach (string recommendation in summary.Recommendations)
        {
            _creatorIntelligenceRecommendations.Add("• " + recommendation);
        }

        CreatorIntelligenceDashboard dashboard = await _creatorIntelligence.AnalyzeDashboardAsync(30);
        ApplyCreatorIntelligenceDashboard(dashboard);
        ApplyCreatorContentPerformance(await _creatorIntelligence.AnalyzeContentPerformanceAsync(30));
        ApplyCreatorEventCorrelations(await _creatorIntelligence.AnalyzeEventCorrelationsAsync(30));
        ApplyCreatorActionPlan(await _creatorIntelligence.AnalyzeActionPlanAsync());
        ApplyCreatorActionEffectiveness(await _creatorIntelligence.AnalyzeActionEffectivenessAsync());
        ApplyCreatorExperiments(await _creatorIntelligence.AnalyzeExperimentsAsync());
    }

    private void ApplyCreatorIntelligenceDashboard(CreatorIntelligenceDashboard dashboard)
    {
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRecentSessionsList.Items.Clear();
        if (dashboard.SessionCount == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceDashboardStatusText.Text = "Keine vollständigen Sessions";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceQualityIndexText.Text = "–";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEngagementIndexText.Text = "–";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceGrowthIndexText.Text = "–";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceForecastText.Text = "–";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligencePeriodText.Text = "Woche: – · Monat: –";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceTrendText.Text = "Trend: Noch keine Daten";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceBestTimeText.Text = "Beste Startzeit: –";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceBestCategoryText.Text = "Beste Kategorie: –";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRecentSessionsList.Items.Add("Noch keine vollständigen Sessions im 30-Tage-Zeitraum.");
            return;
        }

        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceDashboardStatusText.Text = $"{dashboard.SessionCount} Sessions · Ø Score {dashboard.AverageCreatorScore:0.0}";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceQualityIndexText.Text = dashboard.StreamQualityIndex.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEngagementIndexText.Text = dashboard.EngagementIndex.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceGrowthIndexText.Text = dashboard.GrowthIndex.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceForecastText.Text = $"Score {dashboard.PredictedCreatorScore} · Ø {dashboard.PredictedAverageViewers:0.0}";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligencePeriodText.Text = $"Woche: {dashboard.WeeklySessionCount} Streams · Ø Score {dashboard.WeeklyAverageCreatorScore:0.0} · Monat: {dashboard.SessionCount} Streams · Ø Score {dashboard.AverageCreatorScore:0.0}";
        string scoreDirection = dashboard.CreatorScoreTrend > .5 ? "+" : string.Empty;
        string viewerDirection = dashboard.ViewerTrendPerStream > .05 ? "+" : string.Empty;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceTrendText.Text = $"Trend: Score {scoreDirection}{dashboard.CreatorScoreTrend:0.0} · Zuschauer {viewerDirection}{dashboard.ViewerTrendPerStream:0.0} je Stream";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceBestTimeText.Text = $"Beste Startzeit: {dashboard.BestDay.ToGermanDayName()} gegen {dashboard.BestStartHour:00}:00 Uhr";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceBestCategoryText.Text = $"Beste Kategorie: {dashboard.BestCategory} · Ø Bindung {dashboard.AverageRetentionPercent:0}%";

        foreach (CreatorIntelligenceSummary session in dashboard.RecentSessions)
        {
            string category = string.IsNullOrWhiteSpace(session.Category) ? "Ohne Kategorie" : session.Category;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRecentSessionsList.Items.Add(
                $"{session.StartedAt:dd.MM. HH:mm} · Score {session.CreatorScore} · Ø {session.AverageViewers:0.0} · {session.RetentionPercent:0}% Bindung · {category}");
        }

        foreach (string insight in dashboard.Insights)
        {
            _creatorIntelligenceRecommendations.Add("◆ " + insight);
        }
    }


    private void ApplyCreatorContentPerformance(CreatorContentPerformance performance)
    {
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceScenesList.Items.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceTracksList.Items.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceHeatmapList.Items.Clear();

        if (performance.SessionCount == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceScenesList.Items.Add("Noch keine vollständigen Daten.");
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceTracksList.Items.Add("Noch keine vollständigen Daten.");
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceHeatmapList.Items.Add("Noch keine vollständigen Daten.");
            return;
        }

        foreach (CreatorContentPerformanceRow scene in performance.Scenes)
        {
            string delta = scene.ViewerDelta > 0 ? $"+{scene.ViewerDelta:0.0}" : $"{scene.ViewerDelta:0.0}";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceScenesList.Items.Add($"{scene.Name} · {delta} Zuschauer · Ø {scene.AverageViewers:0.0} · {scene.Occurrences}×");
        }
        if (performance.Scenes.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceScenesList.Items.Add("Keine OBS-Szenenwechsel aufgezeichnet.");
        }

        foreach (CreatorContentPerformanceRow track in performance.Tracks)
        {
            string delta = track.ViewerDelta > 0 ? $"+{track.ViewerDelta:0.0}" : $"{track.ViewerDelta:0.0}";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceTracksList.Items.Add($"{track.Name} · {delta} Zuschauer · Ø {track.AverageViewers:0.0}");
        }
        if (performance.Tracks.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceTracksList.Items.Add("Keine Spotify-Titelwechsel aufgezeichnet.");
        }

        foreach (CreatorHeatmapCell cell in performance.Heatmap)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceHeatmapList.Items.Add($"{cell.Day.ToGermanDayName()} {cell.Hour:00}:00 · Ø {cell.AverageViewers:0.0} · {cell.SampleCount} Samples");
        }
        if (performance.Heatmap.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceHeatmapList.Items.Add("Keine Zuschauer-Samples vorhanden.");
        }

        foreach (string insight in performance.Insights)
        {
            _creatorIntelligenceRecommendations.Add("◇ " + insight);
        }
    }


    private void ApplyCreatorEventCorrelations(CreatorEventCorrelationReport report)
    {
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceCorrelationList.Items.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRaidList.Items.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionsList.Items.Clear();

        foreach (CreatorEventCorrelationRow row in report.Correlations)
        {
            string delta5 = row.ViewerDelta5Minutes > 0 ? $"+{row.ViewerDelta5Minutes:0.0}" : $"{row.ViewerDelta5Minutes:0.0}";
            string delta10 = row.ViewerDelta10Minutes > 0 ? $"+{row.ViewerDelta10Minutes:0.0}" : $"{row.ViewerDelta10Minutes:0.0}";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceCorrelationList.Items.Add($"{row.EventName} · 5 Min {delta5} · 10 Min {delta10} · {row.Occurrences}×");
        }
        if (report.Correlations.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceCorrelationList.Items.Add("Noch keine belastbare Ereigniskorrelation.");
        }

        foreach (CreatorRaidRetentionRow raid in report.Raids)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRaidList.Items.Add($"{raid.RaidSummary} · 5m {raid.ViewersAfter5:0} · 10m {raid.ViewersAfter10:0} · 30m {raid.ViewersAfter30:0} · {raid.Retention30Percent:0}%");
        }

        if (report.Raids.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRaidList.Items.Add("Noch keine Raid-Daten mit Zuschauer-Samples.");
        }

        foreach (string action in report.Actions)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionsList.Items.Add(action);
            _creatorIntelligenceRecommendations.Add("▶ " + action);
        }
    }

    private void ApplyCreatorActionPlan(CreatorActionPlan plan)
    {
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionPlanList.Items.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionStatusText.Text = $"{plan.OpenCount} offen · {plan.CompletedCount} erledigt";
        foreach (CreatorActionItem? item in plan.Items.Take(20))
        {
            string priority = item.Priority == 1 ? "HOCH" : item.Priority == 2 ? "MITTEL" : "NORMAL";
            string progress = item.Metric == "manual" ? string.Empty : $" · {item.CurrentValue ?? item.Baseline:0.0}/{item.Target:0.0}";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionPlanList.Items.Add(new CreatorActionListItem(item.Id, $"[{item.Status}] [{priority}] {item.Title}{progress}"));
        }
        if (plan.Items.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionPlanList.Items.Add(new CreatorActionListItem(string.Empty, "Noch keine Maßnahmen vorhanden."));
        }
    }

    private void ApplyCreatorActionEffectiveness(CreatorActionEffectivenessReport report)
    {
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEffectivenessList.Items.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEffectivenessStatusText.Text = $"{report.ImprovedCount} verbessert · {report.ReachedCount} erreicht · {report.DeclinedCount} rückläufig";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEffectivenessSummaryText.Text = report.Summary;
        foreach (CreatorActionEffectivenessRow? row in report.Rows.Take(15))
        {
            string delta = row.Improvement > 0 ? $"+{row.Improvement:0.0}" : $"{row.Improvement:0.0}";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEffectivenessList.Items.Add($"[{row.Status}] {row.Title} · {row.Baseline:0.0} → {row.Current:0.0} · Δ {delta} · {row.ProgressPercent:0}% · {row.Verdict}");
        }
        if (report.Rows.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceEffectivenessList.Items.Add("Noch keine messbaren Maßnahmen vorhanden.");
        }
    }


    private void ApplyCreatorExperiments(CreatorExperimentReport report)
    {
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceExperimentList.Items.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceExperimentStatusText.Text = $"{report.ActiveCount} aktiv · {report.CompletedCount} ausgewertet · {report.PositiveCount} positiv";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceExperimentSummaryText.Text = report.Summary;
        foreach (CreatorExperimentRow? row in report.Rows.Take(15))
        {
            string delta = row.Delta > 0 ? $"+{row.Delta:0.0}" : $"{row.Delta:0.0}";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceExperimentList.Items.Add($"[{row.Status}] {row.Title} · {row.SessionCount}/{row.TargetSessions} Streams · {row.Baseline:0.0} → {row.Current:0.0} · Δ {delta} · {row.Confidence} · {row.Verdict}");
        }
        if (report.Rows.Count == 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceExperimentList.Items.Add("Noch keine Experimente vorhanden.");
        }
    }

    private async Task StartSelectedCreatorExperimentAsync()
    {
        if (ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionPlanList.SelectedItem is not CreatorActionListItem item || string.IsNullOrWhiteSpace(item.Id))
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = "Bitte zuerst eine messbare Maßnahme auswählen.";
            return;
        }
        await _creatorIntelligence.StartExperimentFromActionAsync(item.Id);
        ApplyCreatorExperiments(await _creatorIntelligence.AnalyzeExperimentsAsync());
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = "Experiment gestartet. Die nächsten drei vollständigen Streams werden verglichen.";
    }

    private async Task CompleteSelectedCreatorActionAsync()
    {
        if (ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceActionPlanList.SelectedItem is not CreatorActionListItem item || string.IsNullOrWhiteSpace(item.Id))
        {
            return;
        }

        await _creatorIntelligence.CompleteActionAsync(item.Id);
        ApplyCreatorActionPlan(await _creatorIntelligence.AnalyzeActionPlanAsync());
        ApplyCreatorActionEffectiveness(await _creatorIntelligence.AnalyzeActionEffectivenessAsync());
        ApplyCreatorExperiments(await _creatorIntelligence.AnalyzeExperimentsAsync());
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = "Maßnahme als erledigt markiert.";
    }

    private async Task CreateCreatorIntelligenceWeeklyReportAsync()
    {
        try
        {
            string path = await _creatorIntelligence.GenerateWeeklyReportAsync();
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = "Wochenbericht erstellt.";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = "Wochenbericht konnte nicht erstellt werden: " + ex.Message;
        }
    }

    private async Task AddCreatorIntelligenceNoteAsync()
    {
        string note = ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceNoteBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        await _creatorIntelligence.RecordAsync("session.note", new { note, scene = _servicesObsCurrentScene, viewers = _currentLiveViewerCount });
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceNoteBox.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStatusText.Text = "Session-Notiz gespeichert.";
    }

    private void OpenCreatorIntelligenceFolder()
    {
        Directory.CreateDirectory(_creatorIntelligence.RootDirectory);
        Process.Start(new ProcessStartInfo(_creatorIntelligence.RootDirectory) { UseShellExecute = true });
    }

    private sealed record CreatorActionListItem(string Id, string DisplayText);
}
