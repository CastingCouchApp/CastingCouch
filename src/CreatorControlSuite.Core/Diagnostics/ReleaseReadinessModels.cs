namespace CreatorControlSuite.Core.Diagnostics;
public sealed record ReleaseReadinessItem(string Area,string Status,string Detail,bool Blocking);
public sealed record ReleaseReadinessReport(bool Ready,IReadOnlyList<ReleaseReadinessItem> Items);
