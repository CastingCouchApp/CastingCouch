using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Automation;

/// <summary>
/// Pure schedule evaluation for timed automation rules (no I/O, no UI).
/// </summary>
public static class TimedAutomationSchedule
{
    public static bool IsDue(TimedAutomationRuleSettings rule, DateTime localNow)
    {
        if (!TimeOnly.TryParse(rule.ScheduleTime, out TimeOnly scheduledTime))
        {
            return false;
        }

        var today = DateOnly.FromDateTime(localNow);
        if (DateOnly.TryParse(rule.ActiveFromDate, out DateOnly activeFrom) && today < activeFrom)
        {
            return false;
        }

        if (DateOnly.TryParse(rule.ActiveUntilDate, out DateOnly activeUntil) && today > activeUntil)
        {
            return false;
        }

        if (IsDateExcluded(rule, today) || IsDateInBlackout(rule, today))
        {
            return false;
        }

        var scheduledDateTime = today.ToDateTime(scheduledTime);
        if (localNow < scheduledDateTime)
        {
            return false;
        }

        TimeSpan missedBy = localNow - scheduledDateTime;
        if (string.Equals(rule.MissedRunBehavior, "Skip", StringComparison.OrdinalIgnoreCase) && missedBy > TimeSpan.FromMinutes(1))
        {
            return false;
        }

        if (string.Equals(rule.MissedRunBehavior, "WithinGrace", StringComparison.OrdinalIgnoreCase) &&
            missedBy > TimeSpan.FromMinutes(Math.Clamp(rule.CatchUpGraceMinutes, 0, 1440)))
        {
            return false;
        }

        if (string.Equals(rule.LastScheduledRunDate, localNow.ToString("yyyy-MM-dd"), StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(rule.TriggerType, "WeeklySchedule", StringComparison.OrdinalIgnoreCase))
        {
            string[] days = (rule.ScheduleDays ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!days.Any(x => string.Equals(x, localNow.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (string.Equals(rule.TriggerType, "OneTimeSchedule", StringComparison.OrdinalIgnoreCase))
        {
            return DateOnly.TryParse(rule.ScheduleDate, out DateOnly scheduledDate) && today == scheduledDate;
        }

        return true;
    }

    public static bool IsDateExcluded(TimedAutomationRuleSettings rule, DateOnly day) =>
        (rule.ExcludedDates ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => DateOnly.TryParse(x, out DateOnly excluded) && excluded == day);

    public static bool IsDateInBlackout(TimedAutomationRuleSettings rule, DateOnly day)
    {
        foreach (string range in (rule.BlackoutRanges ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = range.Split("..", StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !DateOnly.TryParse(parts[0], out DateOnly from) ||
                !DateOnly.TryParse(parts[1], out DateOnly until))
            {
                continue;
            }

            if (day >= from && day <= until)
            {
                return true;
            }
        }

        return false;
    }

    public static string DescribeNextRun(TimedAutomationRuleSettings rule, DateTime localNow)
    {
        if (!TimeOnly.TryParse(rule.ScheduleTime, out TimeOnly scheduledTime))
        {
            return "Ungültige Uhrzeit";
        }

        for (int offset = 0; offset < 370; offset++)
        {
            DateTime candidateDay = localNow.Date.AddDays(offset);
            var day = DateOnly.FromDateTime(candidateDay);
            if (DateOnly.TryParse(rule.ActiveFromDate, out DateOnly activeFrom) && day < activeFrom)
            {
                continue;
            }

            if (DateOnly.TryParse(rule.ActiveUntilDate, out DateOnly activeUntil) && day > activeUntil)
            {
                break;
            }

            if (IsDateExcluded(rule, day) || IsDateInBlackout(rule, day))
            {
                continue;
            }

            if (string.Equals(rule.TriggerType, "WeeklySchedule", StringComparison.OrdinalIgnoreCase))
            {
                string[] days = (rule.ScheduleDays ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!days.Any(x => string.Equals(x, candidateDay.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (string.Equals(rule.TriggerType, "OneTimeSchedule", StringComparison.OrdinalIgnoreCase))
            {
                if (!DateOnly.TryParse(rule.ScheduleDate, out DateOnly scheduledDate) || day != scheduledDate)
                {
                    continue;
                }
            }

            DateTime scheduled = day.ToDateTime(scheduledTime);
            if (scheduled < localNow)
            {
                continue;
            }

            if (string.Equals(rule.LastScheduledRunDate, day.ToString("yyyy-MM-dd"), StringComparison.Ordinal) &&
                scheduled.Date == localNow.Date)
            {
                continue;
            }

            return scheduled.ToString("dd.MM.yyyy HH:mm");
        }

        return "Kein nächster Lauf";
    }

    public static bool IsTriggerDue(
        TimedAutomationRuleSettings rule,
        DateTimeOffset nowUtc,
        DateTimeOffset? streamSessionStartedAt,
        DateTimeOffset? sceneActivatedAt,
        string? currentScene)
    {
        if (string.Equals(rule.TriggerType, "StreamElapsed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rule.TriggerType, "StreamStarted", StringComparison.OrdinalIgnoreCase))
        {
            return streamSessionStartedAt.HasValue &&
                   nowUtc - streamSessionStartedAt.Value >= TimeSpan.FromSeconds(
                       string.Equals(rule.TriggerType, "StreamStarted", StringComparison.OrdinalIgnoreCase) ? 0 : rule.DelaySeconds);
        }

        if (string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rule.TriggerType, "SceneActivated", StringComparison.OrdinalIgnoreCase))
        {
            return sceneActivatedAt.HasValue &&
                   string.Equals(currentScene, rule.TriggerScene, StringComparison.OrdinalIgnoreCase) &&
                   nowUtc - sceneActivatedAt.Value >= TimeSpan.FromSeconds(
                       string.Equals(rule.TriggerType, "SceneActivated", StringComparison.OrdinalIgnoreCase) ? 0 : rule.DelaySeconds);
        }

        if (rule.TriggerType is "DailySchedule" or "WeeklySchedule" or "OneTimeSchedule")
        {
            return IsDue(rule, DateTime.Now);
        }

        return false;
    }
}
