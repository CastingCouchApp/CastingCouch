using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.Tests;

public sealed class StreamDeckAutomationRuleServiceTests
{
    [Fact]
    public void IsRuleMatch_CombinesConditionsWithConfiguredOperator()
    {
        var states = new Dictionary<string, bool>
        {
            ["stream.live"] = true,
            ["obs.connected"] = false
        };
        var rule = new StreamDeckAutomationRule
        {
            Condition = "stream.live",
            Condition2 = "obs.connected",
            LogicalOperator = "or"
        };

        Assert.True(
            StreamDeckAutomationRuleService.IsRuleMatch(
                rule,
                states,
                new DateTime(2026, 7, 28, 20, 0, 0)));

        rule.LogicalOperator = "and";
        Assert.False(
            StreamDeckAutomationRuleService.IsRuleMatch(
                rule,
                states,
                new DateTime(2026, 7, 28, 20, 0, 0)));
    }

    [Fact]
    public void IsScheduleActive_HandlesWeekdaysAndOvernightWindows()
    {
        var rule = new StreamDeckAutomationRule
        {
            ActiveDays = "Di",
            ActiveWindow = "22:00-02:00"
        };

        Assert.True(
            StreamDeckAutomationRuleService.IsScheduleActive(
                rule,
                new DateTime(2026, 7, 28, 23, 30, 0)));
        Assert.True(
            StreamDeckAutomationRuleService.IsScheduleActive(
                rule,
                new DateTime(2026, 7, 28, 1, 30, 0)));
        Assert.False(
            StreamDeckAutomationRuleService.IsScheduleActive(
                rule,
                new DateTime(2026, 7, 29, 23, 30, 0)));
    }

    [Fact]
    public void Validate_ReturnsAllStructuralRuleProblems()
    {
        var rule = new StreamDeckAutomationRule
        {
            Id = "broken",
            Profile = "",
            Page = "",
            Priority = 1001,
            DelaySeconds = -1,
            HoldSeconds = 4000,
            Condition = "time.reached",
            Time = "invalid",
            ActiveWindow = "invalid",
            Group = ""
        };

        IReadOnlyList<string> issues =
            StreamDeckAutomationRuleService.Validate([rule]);

        Assert.Equal(6, issues.Count);
        Assert.All(issues, issue => Assert.StartsWith("broken:", issue));
    }
}
