using System.Text.Json;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Tests;

public sealed class OverlayGoalLayoutUpdaterTests
{
    [Fact]
    public void Apply_UpdatesEveryExistingGoalBarAndRemovesCurrentOverride()
    {
        OverlayLayout layout = OverlayLayout.CreateDefault();
        layout.Items.Add(Goal("followers", 200));
        layout.Items.Add(Goal("subs", 25));
        layout.Items.Add(Goal("bits", 100));
        layout.Items.Add(new OverlayLayoutItem { Type = "chat" });

        bool changed = OverlayGoalLayoutUpdater.Apply(
            layout,
            new OverlayGoalPreset("Follower-Ziel", 500),
            new OverlayGoalPreset("Sub-Ziel", 50),
            new OverlayGoalPreset("Neues Mikrofon", 750));

        Assert.True(changed);
        AssertGoal(layout.Items[0], "Follower-Ziel", 500);
        AssertGoal(layout.Items[1], "Sub-Ziel", 50);
        AssertGoal(layout.Items[2], "Neues Mikrofon", 750);
        Assert.Empty(layout.Items[3].Props);
    }

    private static OverlayLayoutItem Goal(string kind, double target) => new()
    {
        Type = "goal-bar",
        Props = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = JsonSerializer.SerializeToElement(kind),
            ["target"] = JsonSerializer.SerializeToElement(target),
            ["current"] = JsonSerializer.SerializeToElement(12)
        }
    };

    private static void AssertGoal(OverlayLayoutItem item, string label, double target)
    {
        Assert.Equal(label, item.Props["label"].GetString());
        Assert.Equal(target, item.Props["target"].GetDouble());
        Assert.False(item.Props.ContainsKey("current"));
    }
}
