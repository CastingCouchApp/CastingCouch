using System.Text.Json;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Modules.Overlay;

public readonly record struct OverlayGoalPreset(string Label, double Target);

public static class OverlayGoalLayoutUpdater
{
    public static bool Apply(
        OverlayLayout layout,
        OverlayGoalPreset follower,
        OverlayGoalPreset subscriptions,
        OverlayGoalPreset donations)
    {
        bool changed = false;
        foreach (OverlayLayoutItem item in layout.Items.Where(item =>
                     string.Equals(item.Type, "goal-bar", StringComparison.OrdinalIgnoreCase)))
        {
            string kind = ReadString(item.Props, "kind", "followers");
            OverlayGoalPreset preset = kind.ToLowerInvariant() switch
            {
                "subs" => subscriptions,
                "bits" or "custom" => donations,
                _ => follower
            };

            item.Props["label"] = JsonSerializer.SerializeToElement(preset.Label);
            item.Props["target"] = JsonSerializer.SerializeToElement(Math.Max(1, preset.Target));
            item.Props.Remove("current");
            changed = true;
        }

        return changed;
    }

    private static string ReadString(
        IReadOnlyDictionary<string, JsonElement> props,
        string key,
        string fallback) =>
        props.TryGetValue(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}
