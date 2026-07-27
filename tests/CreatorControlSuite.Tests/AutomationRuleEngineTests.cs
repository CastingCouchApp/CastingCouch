using CreatorControlSuite.Core.Automation;

namespace CreatorControlSuite.Tests;

public sealed class AutomationRuleEngineTests
{
    [Fact]
    public async Task HandleTriggerAsync_FiresMatchingTrigger()
    {
        var handler = new FakeActionHandler("PlaySound");
        var engine = new AutomationRuleEngine([handler]);
        engine.ReplaceRules(
        [
            new AutomationRule
            {
                Id = "rule-1",
                Name = "Match",
                Enabled = true,
                TriggerType = "TwitchFollow",
                ActionType = "PlaySound"
            }
        ]);

        await engine.HandleTriggerAsync(
            new AutomationContext(
                "TwitchFollow",
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        Assert.Equal(1, handler.ExecuteCalls);
    }

    [Fact]
    public async Task HandleTriggerAsync_SkipsFireOnceSecondTime()
    {
        var handler = new FakeActionHandler("Notify");
        var engine = new AutomationRuleEngine([handler]);
        engine.ReplaceRules(
        [
            new AutomationRule
            {
                Id = "once-1",
                Name = "Once",
                Enabled = true,
                FireOnce = true,
                TriggerType = "StreamLive",
                ActionType = "Notify"
            }
        ]);

        var context = new AutomationContext(
            "StreamLive",
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        await engine.HandleTriggerAsync(context);
        await engine.HandleTriggerAsync(context);

        Assert.Equal(1, handler.ExecuteCalls);
    }

    [Fact]
    public async Task HandleTriggerAsync_RespectsHoldDuration()
    {
        var handler = new FakeActionHandler("CooldownAction");
        var engine = new AutomationRuleEngine([handler]);
        engine.ReplaceRules(
        [
            new AutomationRule
            {
                Id = "hold-1",
                Name = "Hold",
                Enabled = true,
                HoldDuration = TimeSpan.FromSeconds(30),
                TriggerType = "Cheer",
                ActionType = "CooldownAction"
            }
        ]);

        var context = new AutomationContext(
            "Cheer",
            new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

        await engine.HandleTriggerAsync(context);
        await engine.HandleTriggerAsync(context);

        Assert.Equal(1, handler.ExecuteCalls);
    }

    [Fact]
    public async Task HandleTriggerAsync_SkipsUnknownAction()
    {
        var handler = new FakeActionHandler("Known");
        var engine = new AutomationRuleEngine([handler]);
        engine.ReplaceRules(
        [
            new AutomationRule
            {
                Id = "unknown-1",
                Name = "Unknown",
                Enabled = true,
                TriggerType = "Raid",
                ActionType = "DoesNotExist"
            }
        ]);

        await engine.HandleTriggerAsync(
            new AutomationContext(
                "Raid",
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow));

        Assert.Equal(0, handler.ExecuteCalls);
    }

    private sealed class FakeActionHandler : IAutomationActionHandler
    {
        public FakeActionHandler(string actionType) => ActionType = actionType;

        public string ActionType { get; }
        public int ExecuteCalls { get; private set; }

        public Task ExecuteAsync(
            AutomationRule rule,
            AutomationContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.CompletedTask;
        }
    }
}
