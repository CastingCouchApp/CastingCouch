namespace CreatorControlSuite.App.Themes;

public static class ThemeCatalog
{
    public const string ClassicId = "classic";

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new(
            ClassicId,
            "Classic",
            "Dunkles Standard-Theme mit Orange-Akzent.",
            "Themes/Classic.xaml",
            IsPremium: false),
        new(
            "comic-sans-extravaganza",
            "Comic Sans Extravaganza",
            "Lautes Gelb/Cyan/Magenta auf Tiefblau – maximal unernst.",
            "Themes/ComicSansExtravaganza.xaml",
            IsPremium: true),
        new(
            "pink-cage-flair",
            "Pink Cage Flair",
            "Hot Pink auf Schwarz mit Arena-Energy und Impact-Headings.",
            "Themes/PinkCageFlair.xaml",
            IsPremium: true),
        new(
            "vespucci-heights",
            "Vespucci Heights",
            "Vice-City: Teal, Magenta und Sunset auf Nachtblau.",
            "Themes/VespucciHeights.xaml",
            IsPremium: true),
        new(
            "vanilla-unicorn-lounge",
            "Vanilla Unicorn Lounge",
            "Creme, Lavendel und Candy-Pink in weichen Pastells.",
            "Themes/VanillaUnicornLounge.xaml",
            IsPremium: true),
        new(
            "neon-night-market",
            "Neon Night Market",
            "Elektrisches Lime/Violett/Cyan auf Near-Black.",
            "Themes/NeonNightMarket.xaml",
            IsPremium: true),
        new(
            "terminal-green-override",
            "Terminal Green Override",
            "Matrix-Grün auf reinem Schwarz, Consolas-Feel.",
            "Themes/TerminalGreenOverride.xaml",
            IsPremium: true),
        new(
            "blood-moon-broadcast",
            "Blood Moon Broadcast",
            "Blutrot, Kohle und Gold-Akzent mit Georgia.",
            "Themes/BloodMoonBroadcast.xaml",
            IsPremium: true),
        new(
            "pastel-lofi-cafe",
            "Pastel Lo-Fi Café",
            "Warmbeige, Sage und Soft-Coral – gemütlich.",
            "Themes/PastelLofiCafe.xaml",
            IsPremium: true),
        new(
            "gold-rush-studio",
            "Gold Rush Studio",
            "Schwarz, Champagnergold und Elfenbein.",
            "Themes/GoldRushStudio.xaml",
            IsPremium: true),
        new(
            "arctic-glass-lab",
            "Arctic Glass Lab",
            "Eisblau und Frostweiß auf kühlen Glasflächen.",
            "Themes/ArcticGlassLab.xaml",
            IsPremium: true)
    ];

    public static ThemeDefinition Classic => All[0];

    public static ThemeDefinition Resolve(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return Classic;
        }

        return All.FirstOrDefault(t =>
                   string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase))
               ?? Classic;
    }
}
