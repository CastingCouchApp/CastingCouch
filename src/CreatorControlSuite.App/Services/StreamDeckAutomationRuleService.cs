namespace CreatorControlSuite.App.Services;

public sealed class StreamDeckAutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Condition { get; set; } = "stream.live";
    public string Condition2 { get; set; } = string.Empty;
    public string LogicalOperator { get; set; } = "and";
    public string Profile { get; set; } = "Standard";
    public string Page { get; set; } = "Hauptseite";
    public int Priority { get; set; } = 100;
    public int DelaySeconds { get; set; }
    public int HoldSeconds { get; set; } = 10;
    public string Time { get; set; } = "20:00";
    public bool IsFallback { get; set; }
    public bool Enabled { get; set; } = true;
    public string Group { get; set; } = "Standard";
    public string ActiveDays { get; set; } =
        "Mo,Di,Mi,Do,Fr,Sa,So";
    public string ActiveWindow { get; set; } = "00:00-23:59";
    public DateTimeOffset? LastAppliedAt { get; set; }
    public DateTimeOffset? LastEvaluatedAt { get; set; }
    public int MatchCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string DisabledReason { get; set; } = string.Empty;
}

public static class StreamDeckAutomationRuleService
{
    public static bool IsRuleMatch(
        StreamDeckAutomationRule rule,
        IReadOnlyDictionary<string, bool> states,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(states);

        bool first = IsConditionMatch(
            rule.Condition,
            rule,
            states,
            now);
        if (string.IsNullOrWhiteSpace(rule.Condition2))
        {
            return first;
        }

        bool second = IsConditionMatch(
            rule.Condition2,
            rule,
            states,
            now);
        return string.Equals(
            rule.LogicalOperator,
            "or",
            StringComparison.OrdinalIgnoreCase)
            ? first || second
            : first && second;
    }

    public static bool IsValidWindow(string value)
    {
        string[] parts = value.Split(
            '-',
            StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               TimeOnly.TryParse(parts[0], out _) &&
               TimeOnly.TryParse(parts[1], out _);
    }

    public static bool IsScheduleActive(
        StreamDeckAutomationRule rule,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(rule);
        string day = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "Mo",
            DayOfWeek.Tuesday => "Di",
            DayOfWeek.Wednesday => "Mi",
            DayOfWeek.Thursday => "Do",
            DayOfWeek.Friday => "Fr",
            DayOfWeek.Saturday => "Sa",
            _ => "So"
        };
        string[] days = (rule.ActiveDays ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (days.Length > 0 &&
            !days.Contains(day, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = (rule.ActiveWindow ?? "00:00-23:59").Split(
            '-',
            StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !TimeOnly.TryParse(parts[0], out TimeOnly start) ||
            !TimeOnly.TryParse(parts[1], out TimeOnly end))
        {
            return true;
        }

        var current = TimeOnly.FromDateTime(now);
        return start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;
    }

    public static IReadOnlyList<string> Validate(
        IEnumerable<StreamDeckAutomationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var issues = new List<string>();
        foreach (StreamDeckAutomationRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Profile) ||
                string.IsNullOrWhiteSpace(rule.Page))
            {
                issues.Add(
                    $"{rule.Id}: Zielprofil oder Zielseite fehlt.");
            }

            if (rule.Priority is < 0 or > 1000)
            {
                issues.Add(
                    $"{rule.Id}: Priorität außerhalb 0–1000.");
            }

            if (rule.DelaySeconds is < 0 or > 3600 ||
                rule.HoldSeconds is < 0 or > 3600)
            {
                issues.Add(
                    $"{rule.Id}: Verzögerung oder Sperrzeit ungültig.");
            }

            if (rule.Condition == "time.reached" &&
                !TimeOnly.TryParse(rule.Time, out _))
            {
                issues.Add($"{rule.Id}: Uhrzeit ungültig.");
            }

            if (!IsValidWindow(rule.ActiveWindow))
            {
                issues.Add(
                    $"{rule.Id}: Aktivitätszeitraum ungültig.");
            }

            if (string.IsNullOrWhiteSpace(rule.Group))
            {
                issues.Add($"{rule.Id}: Regelgruppe fehlt.");
            }
        }

        return issues;
    }

    private static bool IsConditionMatch(
        string condition,
        StreamDeckAutomationRule rule,
        IReadOnlyDictionary<string, bool> states,
        DateTime now) =>
        condition switch
        {
            "stream.live" =>
                states.GetValueOrDefault("stream.live"),
            "stream.offline" =>
                !states.GetValueOrDefault("stream.live"),
            "obs.connected" =>
                states.GetValueOrDefault("obs.connected"),
            "obs.disconnected" =>
                !states.GetValueOrDefault("obs.connected"),
            "spotify.playing" =>
                states.GetValueOrDefault("spotify.playing"),
            "spotify.paused" =>
                !states.GetValueOrDefault("spotify.playing"),
            "time.reached" =>
                TimeOnly.TryParse(rule.Time, out TimeOnly target) &&
                TimeOnly.FromDateTime(now).Hour == target.Hour &&
                TimeOnly.FromDateTime(now).Minute == target.Minute,
            _ => false
        };
}
