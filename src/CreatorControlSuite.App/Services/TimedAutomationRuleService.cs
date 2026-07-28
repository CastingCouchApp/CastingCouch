using CreatorControlSuite.Core.Automation;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Services;

public static class TimedAutomationRuleService
{
    public static IReadOnlyList<TimedAutomationRuleSettings> SelectDueRules(
        IEnumerable<TimedAutomationRuleSettings> rules,
        IReadOnlySet<string> executedRuleIds,
        DateTimeOffset nowUtc,
        DateTimeOffset? streamSessionStartedAt,
        DateTimeOffset? sceneActivatedAt,
        string? currentScene)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(executedRuleIds);

        return
        [
            .. rules
                .Where(rule => rule.Enabled)
                .Where(rule =>
                    !rule.OncePerStream ||
                    !executedRuleIds.Contains(rule.Id))
                .Where(rule => TimedAutomationSchedule.IsTriggerDue(
                    rule,
                    nowUtc,
                    streamSessionStartedAt,
                    sceneActivatedAt,
                    currentScene))
                .OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Name)
        ];
    }

    public static IReadOnlyList<TimedAutomationRuleSettings>
        SelectWorkflowSteps(
            IEnumerable<TimedAutomationRuleSettings> rules,
            string workflowGroup)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return
        [
            .. rules
                .Where(rule =>
                    rule.Enabled &&
                    string.Equals(
                        rule.WorkflowGroup,
                        workflowGroup,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(rule => rule.WorkflowOrder)
                .ThenByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<TimedAutomationRuleSettings> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var issues = new List<string>();
        var ids = rules
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (TimedAutomationRuleSettings rule in rules)
        {
            ValidateRule(rule, ids, issues);
        }

        foreach (TimedAutomationRuleSettings rule in rules)
        {
            ValidateNextRuleChain(rule, rules, issues);
        }

        return issues;
    }

    private static void ValidateRule(
        TimedAutomationRuleSettings rule,
        IReadOnlySet<string> ids,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            issues.Add("Hinweis: Eine Regel hat keinen Namen.");
        }

        if (rule.TriggerType is "SceneElapsed" or "SceneActivated" &&
            string.IsNullOrWhiteSpace(rule.TriggerScene))
        {
            issues.Add($"Fehlt: Ausgangsszene bei '{rule.Name}'.");
        }

        bool scheduleTrigger =
            rule.TriggerType is
                "DailySchedule" or
                "WeeklySchedule" or
                "OneTimeSchedule";
        if (scheduleTrigger && !TimeOnly.TryParse(rule.ScheduleTime, out _))
        {
            issues.Add($"Ungültige Uhrzeit bei '{rule.Name}'.");
        }

        if (rule.TriggerType == "WeeklySchedule" &&
            string.IsNullOrWhiteSpace(rule.ScheduleDays))
        {
            issues.Add($"Keine Wochentage bei '{rule.Name}'.");
        }

        if (rule.TriggerType == "OneTimeSchedule" &&
            !DateOnly.TryParse(rule.ScheduleDate, out _))
        {
            issues.Add($"Ungültiges einmaliges Datum bei '{rule.Name}'.");
        }

        if (DateOnly.TryParse(rule.ActiveFromDate, out DateOnly fromDate) &&
            DateOnly.TryParse(rule.ActiveUntilDate, out DateOnly untilDate) &&
            fromDate > untilDate)
        {
            issues.Add($"Aktivzeitraum ist umgekehrt bei '{rule.Name}'.");
        }

        ValidateExcludedDates(rule, issues);
        ValidateBlackoutRanges(rule, issues);
        ValidateAction(rule, issues);
        ValidateReferences(rule, ids, issues);
    }

    private static void ValidateExcludedDates(
        TimedAutomationRuleSettings rule,
        ICollection<string> issues)
    {
        foreach (string value in (rule.ExcludedDates ?? "").Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            if (!DateOnly.TryParse(value, out _))
            {
                issues.Add(
                    $"Ungültiger Ausnahmetag '{value}' bei '{rule.Name}'.");
            }
        }
    }

    private static void ValidateBlackoutRanges(
        TimedAutomationRuleSettings rule,
        ICollection<string> issues)
    {
        foreach (string range in (rule.BlackoutRanges ?? "").Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            string[] bounds = range.Split(
                "..",
                StringSplitOptions.TrimEntries);
            if (bounds.Length != 2 ||
                !DateOnly.TryParse(bounds[0], out DateOnly start) ||
                !DateOnly.TryParse(bounds[1], out DateOnly end) ||
                start > end)
            {
                issues.Add(
                    $"Ungültiger Sperrzeitraum '{range}' bei '{rule.Name}'.");
            }
        }
    }

    private static void ValidateAction(
        TimedAutomationRuleSettings rule,
        ICollection<string> issues)
    {
        if (rule.ActionType == "SwitchScene" &&
            string.IsNullOrWhiteSpace(rule.TargetScene))
        {
            issues.Add($"Fehlt: Zielszene bei '{rule.Name}'.");
        }

        if (rule.ActionType == "SetSourceVisibility" &&
            (string.IsNullOrWhiteSpace(rule.ObsScene) ||
             string.IsNullOrWhiteSpace(rule.ObsSource)))
        {
            issues.Add($"Fehlt: Szene/Quelle bei '{rule.Name}'.");
        }

        if (rule.ActionType == "SetInputMute" &&
            string.IsNullOrWhiteSpace(rule.ObsInput))
        {
            issues.Add($"Fehlt: Audioquelle bei '{rule.Name}'.");
        }

        if (rule.ConditionType == "CurrentScene" &&
            string.IsNullOrWhiteSpace(rule.ConditionValue))
        {
            issues.Add(
                $"Fehlt: Szenenname in Bedingung bei '{rule.Name}'.");
        }

        if (rule.StartWorkflowGroup &&
            string.IsNullOrWhiteSpace(rule.WorkflowGroup))
        {
            issues.Add(
                $"Workflow-Start ohne Gruppenname bei '{rule.Name}'.");
        }
    }

    private static void ValidateReferences(
        TimedAutomationRuleSettings rule,
        IReadOnlySet<string> ids,
        ICollection<string> issues)
    {
        ValidateReference(
            rule,
            rule.NextRuleId,
            ids,
            "Ungültige Folgeregel",
            issues);
        ValidateReference(
            rule,
            rule.DependencyRuleId,
            ids,
            "Ungültige Abhängigkeitsregel",
            issues);
        ValidateReference(
            rule,
            rule.FailureRuleId,
            ids,
            "Ungültige Ersatzregel",
            issues);
        ValidateReference(
            rule,
            rule.RollbackRuleId,
            ids,
            "Ungültige Rückabwicklungsregel",
            issues);
        ValidateSelfReference(
            rule,
            rule.DependencyRuleId,
            "Selbstabhängigkeit",
            issues);
        ValidateSelfReference(
            rule,
            rule.FailureRuleId,
            "Ersatzregel verweist auf sich selbst",
            issues);
        ValidateSelfReference(
            rule,
            rule.RollbackRuleId,
            "Rückabwicklungsregel verweist auf sich selbst",
            issues);
    }

    private static void ValidateReference(
        TimedAutomationRuleSettings rule,
        string referenceId,
        IReadOnlySet<string> ids,
        string message,
        ICollection<string> issues)
    {
        if (!string.IsNullOrWhiteSpace(referenceId) &&
            !ids.Contains(referenceId))
        {
            issues.Add($"{message} bei '{rule.Name}'.");
        }
    }

    private static void ValidateSelfReference(
        TimedAutomationRuleSettings rule,
        string referenceId,
        string message,
        ICollection<string> issues)
    {
        if (string.Equals(
                referenceId,
                rule.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{message} bei '{rule.Name}'.");
        }
    }

    private static void ValidateNextRuleChain(
        TimedAutomationRuleSettings rule,
        IReadOnlyList<TimedAutomationRuleSettings> rules,
        ICollection<string> issues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TimedAutomationRuleSettings? current = rule;
        while (current is not null &&
               !string.IsNullOrWhiteSpace(current.NextRuleId))
        {
            if (!seen.Add(current.Id))
            {
                issues.Add(
                    $"Schleife erkannt, beginnend bei '{rule.Name}'.");
                return;
            }

            current = rules.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    current.NextRuleId,
                    StringComparison.OrdinalIgnoreCase));
        }
    }
}
