namespace CreatorControlSuite.Core.Diagnostics;

public enum InstallerSelfTestStatus { Passed, Warning, Failed }
public sealed record InstallerSelfTestItem(string Check, InstallerSelfTestStatus Status, string Detail, string Recommendation);
public sealed record InstallerSelfTestReport(DateTimeOffset StartedAt, DateTimeOffset CompletedAt, bool Passed, IReadOnlyList<InstallerSelfTestItem> Items);
