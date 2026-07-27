using CreatorControlSuite.Core.Eventing;

namespace CreatorControlSuite.App.Core.Eventing;

public sealed record NavigationRequested(string PageKey);

public sealed record TimedAutomationTick(DateTimeOffset OccurredAt);
