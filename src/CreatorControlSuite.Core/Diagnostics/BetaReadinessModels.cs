namespace CreatorControlSuite.Core.Diagnostics;
public sealed record BetaReadinessArea(string Area,int Passed,int Warnings,int Failed,int ScorePercent,string Detail);
public sealed record BetaReadinessDashboard(DateTimeOffset GeneratedAt,int OverallScorePercent,bool BetaReady,IReadOnlyList<BetaReadinessArea> Areas,IReadOnlyList<string> Blockers);
