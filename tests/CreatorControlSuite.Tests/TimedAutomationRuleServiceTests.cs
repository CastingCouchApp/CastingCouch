using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class TimedAutomationRuleServiceTests
{
    [Fact]
    public void SelectDueRules_FiltersStateAndUsesStablePriorityOrder()
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var executed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "already-run"
        };
        TimedAutomationRuleSettings[] rules =
        [
            Rule("low", "Low", priority: 1),
            Rule("high", "High", priority: 20),
            Rule("already-run", "Executed", priority: 100),
            Rule("disabled", "Disabled", priority: 200, enabled: false)
        ];

        IReadOnlyList<TimedAutomationRuleSettings> due =
            TimedAutomationRuleService.SelectDueRules(
                rules,
                executed,
                DateTimeOffset.UtcNow,
                startedAt,
                sceneActivatedAt: null,
                currentScene: null);

        Assert.Equal(["high", "low"], due.Select(rule => rule.Id));
    }

    [Fact]
    public void SelectWorkflowSteps_FiltersGroupAndOrdersWorkflow()
    {
        TimedAutomationRuleSettings[] rules =
        [
            GroupRule("third", order: 2, priority: 10),
            GroupRule("second", order: 1, priority: 1),
            GroupRule("first", order: 1, priority: 20),
            GroupRule("disabled", order: 0, priority: 100, enabled: false),
            new() { Id = "other", WorkflowGroup = "Other", Enabled = true }
        ];

        IReadOnlyList<TimedAutomationRuleSettings> steps =
            TimedAutomationRuleService.SelectWorkflowSteps(rules, "Show");

        Assert.Equal(
            ["first", "second", "third"],
            steps.Select(rule => rule.Id));
    }

    [Fact]
    public void Validate_ReportsStructuralAndReferenceProblems()
    {
        var first = new TimedAutomationRuleSettings
        {
            Id = "first",
            Name = "",
            TriggerType = "WeeklySchedule",
            ScheduleTime = "invalid",
            ScheduleDays = "",
            ActionType = "SwitchScene",
            TargetScene = "",
            NextRuleId = "second",
            DependencyRuleId = "missing",
            FailureRuleId = "first",
            StartWorkflowGroup = true,
            WorkflowGroup = ""
        };
        var second = new TimedAutomationRuleSettings
        {
            Id = "second",
            Name = "Zweite Regel",
            TriggerType = "StreamStarted",
            ActionType = "OverlayCountdown",
            NextRuleId = "first"
        };

        IReadOnlyList<string> issues =
            TimedAutomationRuleService.Validate([first, second]);

        Assert.Contains("Hinweis: Eine Regel hat keinen Namen.", issues);
        Assert.Contains(
            issues,
            issue => issue.Contains("Ungültige Uhrzeit", StringComparison.Ordinal));
        Assert.Contains(
            issues,
            issue => issue.Contains("Ungültige Abhängigkeitsregel", StringComparison.Ordinal));
        Assert.Contains(
            issues,
            issue => issue.Contains("Ersatzregel verweist auf sich selbst", StringComparison.Ordinal));
        Assert.Contains(
            issues,
            issue => issue.Contains("Schleife erkannt", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ReturnsNoIssuesForValidRule()
    {
        var rule = new TimedAutomationRuleSettings
        {
            Id = "valid",
            Name = "Countdown",
            TriggerType = "StreamStarted",
            ActionType = "OverlayCountdown"
        };

        Assert.Empty(TimedAutomationRuleService.Validate([rule]));
    }

    [Fact]
    public void EvaluateDependency_RequiresExistingRuleWithExpectedStatus()
    {
        var dependency = new TimedAutomationRuleSettings
        {
            Id = "prepare",
            LastRunStatus = "Erfolgreich"
        };
        var rule = new TimedAutomationRuleSettings
        {
            DependencyRuleId = "prepare",
            DependencyRequiredStatus = "Erfolgreich"
        };

        Assert.True(
            TimedAutomationRuntimeService.EvaluateDependency(
                rule,
                [dependency]).CanRun);

        dependency.LastRunStatus = "Fehler";
        TimedAutomationDependencyDecision wrongStatus =
            TimedAutomationRuntimeService.EvaluateDependency(
                rule,
                [dependency]);
        TimedAutomationDependencyDecision missing =
            TimedAutomationRuntimeService.EvaluateDependency(rule, []);

        Assert.False(wrongStatus.CanRun);
        Assert.Equal("Abhängigkeit nicht erfüllt", wrongStatus.Status);
        Assert.False(missing.CanRun);
    }

    [Fact]
    public void ResolveExecutionPolicy_ClampsRuntimeLimits()
    {
        var rule = new TimedAutomationRuleSettings
        {
            TimeoutSeconds = 100_000,
            RetryCount = 50,
            RetryDelaySeconds = -4
        };

        TimedAutomationExecutionPolicy policy =
            TimedAutomationRuntimeService.ResolveExecutionPolicy(rule);

        Assert.Equal(86_400, policy.TimeoutSeconds);
        Assert.Equal(21, policy.MaxAttempts);
        Assert.Equal(0, policy.RetryDelaySeconds);
    }

    [Fact]
    public void SelectStreamEndResetRules_FiltersEnabledResetRules()
    {
        TimedAutomationRuleSettings[] rules =
        [
            new() { Id = "reset", Enabled = true, ResetSourceAtStreamEnd = true },
            new() { Id = "disabled", Enabled = false, ResetSourceAtStreamEnd = true },
            new() { Id = "keep", Enabled = true, ResetSourceAtStreamEnd = false }
        ];

        IReadOnlyList<TimedAutomationRuleSettings> selected =
            TimedAutomationRuntimeService.SelectStreamEndResetRules(rules);

        Assert.Equal(["reset"], selected.Select(rule => rule.Id));
    }

    private static TimedAutomationRuleSettings Rule(
        string id,
        string name,
        int priority,
        bool enabled = true) =>
        new()
        {
            Id = id,
            Name = name,
            Enabled = enabled,
            Priority = priority,
            OncePerStream = true,
            TriggerType = "StreamStarted"
        };

    private static TimedAutomationRuleSettings GroupRule(
        string id,
        int order,
        int priority,
        bool enabled = true) =>
        new()
        {
            Id = id,
            Name = id,
            Enabled = enabled,
            WorkflowGroup = "Show",
            WorkflowOrder = order,
            Priority = priority
        };
}
