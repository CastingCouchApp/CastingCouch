using CreatorControlSuite.Core.Automation;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class TimedAutomationScheduleTests
{
    [Fact]
    public void IsDue_Rejects_InvalidScheduleTime()
    {
        var rule = new TimedAutomationRuleSettings
        {
            TriggerType = "DailySchedule",
            ScheduleTime = "not-a-time"
        };

        Assert.False(TimedAutomationSchedule.IsDue(rule, DateTime.Now));
    }

    [Fact]
    public void IsDue_DailySchedule_True_WithinSameDayWindow()
    {
        DateTime now = new(2026, 7, 27, 20, 5, 0);
        var rule = new TimedAutomationRuleSettings
        {
            TriggerType = "DailySchedule",
            ScheduleTime = "20:00",
            MissedRunBehavior = "SameDay",
            LastScheduledRunDate = ""
        };

        Assert.True(TimedAutomationSchedule.IsDue(rule, now));
    }

    [Fact]
    public void IsDue_Skips_ExcludedDate()
    {
        DateTime now = new(2026, 7, 27, 20, 5, 0);
        var rule = new TimedAutomationRuleSettings
        {
            TriggerType = "DailySchedule",
            ScheduleTime = "20:00",
            ExcludedDates = "2026-07-27"
        };

        Assert.False(TimedAutomationSchedule.IsDue(rule, now));
    }

    [Fact]
    public void IsTriggerDue_StreamStarted_WhenSessionActive()
    {
        var rule = new TimedAutomationRuleSettings
        {
            TriggerType = "StreamStarted",
            DelaySeconds = 0
        };
        DateTimeOffset started = DateTimeOffset.UtcNow.AddMinutes(-1);

        Assert.True(TimedAutomationSchedule.IsTriggerDue(rule, DateTimeOffset.UtcNow, started, null, null));
        Assert.False(TimedAutomationSchedule.IsTriggerDue(rule, DateTimeOffset.UtcNow, null, null, null));
    }

    [Fact]
    public void DescribeNextRun_ReturnsFormattedDate()
    {
        DateTime now = new(2026, 7, 27, 10, 0, 0);
        var rule = new TimedAutomationRuleSettings
        {
            TriggerType = "DailySchedule",
            ScheduleTime = "20:00"
        };

        string next = TimedAutomationSchedule.DescribeNextRun(rule, now);
        Assert.Equal("27.07.2026 20:00", next);
    }
}
