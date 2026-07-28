using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Services;

public sealed record TimedAutomationDependencyDecision(
    bool CanRun,
    string Status);

public sealed record TimedAutomationExecutionPolicy(
    int TimeoutSeconds,
    int MaxAttempts,
    int RetryDelaySeconds);

public static class TimedAutomationRuntimeService
{
    public static TimedAutomationDependencyDecision EvaluateDependency(
        TimedAutomationRuleSettings rule,
        IEnumerable<TimedAutomationRuleSettings> rules)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(rules);
        if (string.IsNullOrWhiteSpace(rule.DependencyRuleId))
        {
            return new(true, "");
        }

        TimedAutomationRuleSettings? dependency =
            rules.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    rule.DependencyRuleId,
                    StringComparison.OrdinalIgnoreCase));
        bool canRun =
            dependency is not null &&
            string.Equals(
                dependency.LastRunStatus,
                rule.DependencyRequiredStatus,
                StringComparison.OrdinalIgnoreCase);
        return new(
            canRun,
            canRun ? "" : "Abhängigkeit nicht erfüllt");
    }

    public static TimedAutomationExecutionPolicy ResolveExecutionPolicy(
        TimedAutomationRuleSettings rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new(
            Math.Clamp(rule.TimeoutSeconds, 1, 86_400),
            Math.Clamp(rule.RetryCount, 0, 20) + 1,
            Math.Clamp(rule.RetryDelaySeconds, 0, 86_400));
    }

    public static IReadOnlyList<TimedAutomationRuleSettings>
        SelectStreamEndResetRules(
            IEnumerable<TimedAutomationRuleSettings> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return
        [
            .. rules.Where(rule =>
                rule.Enabled &&
                rule.ResetSourceAtStreamEnd)
        ];
    }
}
