using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Services;

public sealed class SpotifySceneMusicRow(string sceneName)
{
    public string SceneName { get; } = sceneName;
    public bool Enabled { get; set; }
    public string PlaylistUri { get; set; } = "";
    public bool Shuffle { get; set; }
    public int VolumePercent { get; set; } = 75;
    public bool FadeEnabled { get; set; }
    public int FadeMilliseconds { get; set; } = 500;
}

public static class SpotifySceneMusicRuleService
{
    public static IReadOnlyList<SpotifySceneMusicRow> CreateRows(
        IEnumerable<string> sceneNames,
        IEnumerable<SpotifyAutomationRuleSettings> rules)
    {
        Dictionary<string, SpotifyAutomationRuleSettings> byScene = rules
            .Where(IsScenePlaylistRule)
            .GroupBy(rule => rule.TriggerValue, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return sceneNames
            .Where(scene => !string.IsNullOrWhiteSpace(scene))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(scene =>
            {
                var row = new SpotifySceneMusicRow(scene);
                if (byScene.TryGetValue(scene, out SpotifyAutomationRuleSettings? rule))
                {
                    row.Enabled = rule.Enabled;
                    row.PlaylistUri = rule.PlaylistUri;
                    row.Shuffle = rule.Shuffle;
                    row.VolumePercent = Math.Clamp(rule.VolumePercent, 0, 100);
                    row.FadeEnabled = rule.FadeEnabled;
                    row.FadeMilliseconds = Math.Clamp(rule.FadeMilliseconds, 0, 60_000);
                }

                return row;
            })
            .ToList();
    }

    public static IReadOnlyList<SpotifyAutomationRuleSettings> ApplyRows(
        IEnumerable<SpotifyAutomationRuleSettings> existingRules,
        IEnumerable<SpotifySceneMusicRow> rows)
    {
        List<SpotifyAutomationRuleSettings> result = existingRules
            .Where(rule => !IsScenePlaylistRule(rule))
            .ToList();

        result.AddRange(rows
            .Where(row => row.Enabled && !string.IsNullOrWhiteSpace(row.PlaylistUri))
            .Select(row => new SpotifyAutomationRuleSettings
            {
                Name = $"Szenenmusik: {row.SceneName}",
                Enabled = true,
                TriggerType = "ObsSceneChanged",
                TriggerValue = row.SceneName,
                ActionType = "StartPlaylist",
                PlaylistUri = row.PlaylistUri,
                Shuffle = row.Shuffle,
                VolumePercent = Math.Clamp(row.VolumePercent, 0, 100),
                FadeEnabled = row.FadeEnabled,
                FadeMilliseconds = Math.Clamp(row.FadeMilliseconds, 0, 60_000)
            }));

        return result;
    }

    private static bool IsScenePlaylistRule(SpotifyAutomationRuleSettings rule) =>
        string.Equals(rule.TriggerType, "ObsSceneChanged", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(rule.ActionType, "StartPlaylist", StringComparison.OrdinalIgnoreCase);
}
