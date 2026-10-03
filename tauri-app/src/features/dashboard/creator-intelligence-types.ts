export type IntelligenceSummary = {
    SessionId: string;
    StartedAt: string;
    EndedAt: string;
    Title: string;
    Category: string;
    Duration: string;
    CreatorScore: number;
    PeakViewers: number;
    AverageViewers: number;
    RetentionPercent: number;
    ChatMessagesPerHour: number;
    FollowersPerHour: number;
    ChatMessages: number;
    Followers: number;
    DistinctScenes: number;
    TracksPlayed: number;
    Recommendations: string[];
};
export type ContentRow = {
    Kind: string;
    Name: string;
    Occurrences: number;
    TotalMinutes: number;
    AverageViewers: number;
    ViewerDelta: number;
    ChatMessagesPerMinute: number;
};
export type ActionItem = {
    Id: string;
    Title: string;
    Metric: string;
    Baseline: number;
    Target: number;
    Priority: number;
    Status: string;
    CreatedAt: string;
    CompletedAt: string | null;
    CurrentValue: number | null;
};
export type IntelligenceSnapshot = {
    latest: IntelligenceSummary | null;
    recording: boolean;
    warnings: string[];
    directory: string;
    dashboard: {
        LookbackDays: number;
        SessionCount: number;
        WeeklySessionCount: number;
        WeeklyAverageCreatorScore: number;
        AverageCreatorScore: number;
        StreamQualityIndex: number;
        EngagementIndex: number;
        GrowthIndex: number;
        AverageRetentionPercent: number;
        AverageChatMessagesPerHour: number;
        AverageFollowersPerHour: number;
        AverageViewers: number;
        CreatorScoreTrend: number;
        ViewerTrendPerStream: number;
        BestStartHour: number;
        BestDay: number;
        BestCategory: string;
        PredictedAverageViewers: number;
        PredictedCreatorScore: number;
        RecentSessions: IntelligenceSummary[];
        Insights: string[];
    };
    content: {
        LookbackDays: number;
        SessionCount: number;
        Scenes: ContentRow[];
        Tracks: ContentRow[];
        Heatmap: {
            Day: number;
            Hour: number;
            SampleCount: number;
            AverageViewers: number;
        }[];
        Insights: string[];
    };
    correlation: {
        LookbackDays: number;
        SessionCount: number;
        Correlations: {
            EventName: string;
            EventType: string;
            Occurrences: number;
            BaselineViewers: number;
            ViewerDelta5Minutes: number;
            ViewerDelta10Minutes: number;
        }[];
        Raids: {
            RaidSummary: string;
            ViewersBefore: number;
            ViewersAfter5: number;
            ViewersAfter10: number;
            ViewersAfter30: number;
            Retention30Percent: number;
        }[];
        Actions: string[];
    };
    actions: {
        Items: ActionItem[];
        OpenCount: number;
        CompletedCount: number;
    } | null;
    effectiveness: {
        Rows: {
            Id: string;
            Title: string;
            Metric: string;
            Status: string;
            Baseline: number;
            Current: number;
            Target: number;
            Improvement: number;
            ProgressPercent: number;
            Verdict: string;
            CreatedAt: string;
            CompletedAt: string | null;
        }[];
        ImprovedCount: number;
        DeclinedCount: number;
        ReachedCount: number;
        Summary: string;
    } | null;
    experiments: {
        Rows: {
            Id: string;
            ActionId: string;
            Title: string;
            Metric: string;
            Status: string;
            Baseline: number;
            Current: number;
            Delta: number;
            SessionCount: number;
            TargetSessions: number;
            Confidence: string;
            Verdict: string;
            StartedAt: string;
            CompletedAt: string | null;
        }[];
        ActiveCount: number;
        CompletedCount: number;
        PositiveCount: number;
        Summary: string;
    } | null;
};
