namespace CreatorControlSuite.Core.Licensing;

public static class FeatureCatalog
{
    public const string Obs = "obs", Twitch = "twitch", Spotify = "spotify", YouTubeMusic = "ytmusic", Alerts = "alerts", Overlay = "overlay", Workflow = "workflow", Profiles = "profiles", StreamDeck = "streamdeck", Updates = "updates", Migration = "migration", Diagnostics = "diagnostics", PremiumThemes = "premium-themes", CommercialUse = "commercial-use";
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Editions { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["Core"] = [Obs, Overlay, Workflow, Diagnostics],
        ["Creator"] = [Obs, Twitch, Spotify, YouTubeMusic, Alerts, Overlay, Workflow, Profiles, StreamDeck, Updates, Diagnostics],
        ["Pro"] = [Obs, Twitch, Spotify, YouTubeMusic, Alerts, Overlay, Workflow, Profiles, StreamDeck, Updates, Migration, Diagnostics, PremiumThemes, CommercialUse]
    };
    public static IReadOnlyList<string> ResolveEdition(string edition) => Editions.TryGetValue(edition, out IReadOnlyList<string>? f) ? f : [];
}
