using System.Text.Json;

namespace CreatorControlSuite.App.Services.CreatorIntelligence;

public sealed record ViewerPoint(DateTimeOffset TimestampUtc, int Viewers, string Scene);
public sealed record CreatorContentPerformanceRow(
    string Kind,
    string Name,
    int Occurrences,
    double TotalMinutes,
    double AverageViewers,
    double ViewerDelta,
    double ChatMessagesPerMinute);
public sealed record CreatorHeatmapCell(DayOfWeek Day, int Hour, int SampleCount, double AverageViewers);
public sealed record CreatorContentPerformance(
    int LookbackDays,
    int SessionCount,
    IReadOnlyList<CreatorContentPerformanceRow> Scenes,
    IReadOnlyList<CreatorContentPerformanceRow> Tracks,
    IReadOnlyList<CreatorHeatmapCell> Heatmap,
    IReadOnlyList<string> Insights)
{
    public static CreatorContentPerformance Empty(int lookbackDays) => new(
        lookbackDays, 0,
        [],
        [],
        [],
        ["Noch keine vollständigen Sessions für die Inhaltsanalyse vorhanden."]);
}


public sealed record CreatorEventCorrelationRow(
    string EventName,
    string EventType,
    int Occurrences,
    double BaselineViewers,
    double ViewerDelta5Minutes,
    double ViewerDelta10Minutes);
public sealed record CreatorRaidRetentionRow(
    string RaidSummary,
    double ViewersBefore,
    double ViewersAfter5,
    double ViewersAfter10,
    double ViewersAfter30)
{
    public double Retention30Percent => ViewersAfter5 <= 0 ? 0 : Math.Clamp(ViewersAfter30 / ViewersAfter5 * 100, 0, 250);
}
public sealed record CreatorEventCorrelationReport(
    int LookbackDays,
    int SessionCount,
    IReadOnlyList<CreatorEventCorrelationRow> Correlations,
    IReadOnlyList<CreatorRaidRetentionRow> Raids,
    IReadOnlyList<string> Actions)
{
    public static CreatorEventCorrelationReport Empty(int lookbackDays) => new(
        lookbackDays, 0,
        [],
        [],
        ["Noch keine vollständigen Sessions für die Ereigniskorrelation vorhanden."]);
}

public sealed record CreatorActionItem(
    string Id,
    string Title,
    string Metric,
    double Baseline,
    double Target,
    int Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    double? CurrentValue);
public sealed record CreatorActionPlan(IReadOnlyList<CreatorActionItem> Items, int OpenCount, int CompletedCount);
public sealed record CreatorActionEffectivenessRow(
    string Id,
    string Title,
    string Metric,
    string Status,
    double Baseline,
    double Current,
    double Target,
    double Improvement,
    double ProgressPercent,
    string Verdict,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
public sealed record CreatorActionEffectivenessReport(
    IReadOnlyList<CreatorActionEffectivenessRow> Rows,
    int ImprovedCount,
    int DeclinedCount,
    int ReachedCount,
    string Summary);

public sealed record CreatorIntelligenceEvent(DateTimeOffset TimestampUtc, string SessionId, string Type, JsonElement? Payload);

public sealed record CreatorExperiment(
    string Id,
    string ActionId,
    string Title,
    string Metric,
    double Baseline,
    int TargetSessions,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record CreatorExperimentRow(
    string Id,
    string ActionId,
    string Title,
    string Metric,
    string Status,
    double Baseline,
    double Current,
    double Delta,
    int SessionCount,
    int TargetSessions,
    string Confidence,
    string Verdict,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record CreatorExperimentReport(
    IReadOnlyList<CreatorExperimentRow> Rows,
    int ActiveCount,
    int CompletedCount,
    int PositiveCount,
    string Summary);

public sealed record CreatorIntelligenceSummary(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Title,
    string Category,
    TimeSpan Duration,
    int CreatorScore,
    int PeakViewers,
    double AverageViewers,
    double RetentionPercent,
    double ChatMessagesPerHour,
    double FollowersPerHour,
    int ChatMessages,
    int Followers,
    int DistinctScenes,
    int TracksPlayed,
    IReadOnlyList<string> Recommendations);

public sealed record CreatorIntelligenceDashboard(
    int LookbackDays,
    int SessionCount,
    int WeeklySessionCount,
    double WeeklyAverageCreatorScore,
    double AverageCreatorScore,
    int StreamQualityIndex,
    int EngagementIndex,
    int GrowthIndex,
    double AverageRetentionPercent,
    double AverageChatMessagesPerHour,
    double AverageFollowersPerHour,
    double AverageViewers,
    double CreatorScoreTrend,
    double ViewerTrendPerStream,
    int BestStartHour,
    DayOfWeek BestDay,
    string BestCategory,
    double PredictedAverageViewers,
    int PredictedCreatorScore,
    IReadOnlyList<CreatorIntelligenceSummary> RecentSessions,
    IReadOnlyList<string> Insights)
{
    public static CreatorIntelligenceDashboard Empty(int lookbackDays) => new(
        lookbackDays, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DayOfWeek.Monday, "–", 0, 0,
        [],
        ["Noch keine vollständigen Sessions im gewählten Zeitraum vorhanden."]);
}

public static class CreatorIntelligenceFormattingExtensions
{
    public static string ToGermanDayName(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        DayOfWeek.Sunday => "Sonntag",
        _ => day.ToString()
    };
}
