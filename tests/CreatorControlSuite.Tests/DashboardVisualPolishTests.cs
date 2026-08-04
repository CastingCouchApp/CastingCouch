using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CreatorControlSuite.App.Themes;

namespace CreatorControlSuite.Tests;

public sealed class DashboardVisualPolishTests
{
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SidebarNavButtonStyle_DisablesDefaultFocusVisual()
    {
        string appXaml = ReadAppXaml();

        Assert.Contains(
            "x:Key=\"SidebarNavButtonStyle\"",
            appXaml,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"SidebarNavButtonStyle[\s\S]*?Setter\s+Property\s*=\s*""FocusVisualStyle""\s+Value\s*=\s*""\{x:Null\}""",
                RegexOptions.CultureInvariant),
            appXaml);
    }

    [Fact]
    public void ListBoxItem_SelectedUsesAccentSelectedForeground()
    {
        string appXaml = ReadAppXaml();

        Assert.Contains(
            "TargetType=\"ListBoxItem\"",
            appXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource AccentSelectedForegroundBrush",
            appXaml,
            StringComparison.Ordinal);

        int selectedBlock = appXaml.IndexOf(
            "TargetType=\"ListBoxItem\"",
            StringComparison.Ordinal);
        Assert.True(selectedBlock >= 0);
        string listBoxItemStyle = appXaml[selectedBlock..];
        int nextStyle = listBoxItemStyle.IndexOf(
            "TargetType=\"DataGridRow\"",
            StringComparison.Ordinal);
        if (nextStyle > 0)
        {
            listBoxItemStyle = listBoxItemStyle[..nextStyle];
        }

        Assert.Contains(
            "DynamicResource AccentSelectedForegroundBrush",
            listBoxItemStyle,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DynamicResource TextOnAccentBrush",
            listBoxItemStyle,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardLists_UseListBackgroundBrush()
    {
        string dashboardXaml = ReadDashboardXaml();

        foreach (string listName in new[]
                 {
                     "DashboardTwitchUsersList",
                     "DashboardTwitchEventsList",
                     "DashboardTwitchChatList"
                 })
        {
            Assert.Matches(
                new Regex(
                    $@"x:Name=""{listName}""[^>]*Background=""\{{DynamicResource ListBackgroundBrush\}}""",
                    RegexOptions.CultureInvariant),
                dashboardXaml);
        }
    }

    [Fact]
    public void DashboardChatAndEvents_DisableSelectionHighlightNoise()
    {
        string dashboardXaml = ReadDashboardXaml();

        Assert.Contains(
            "x:Name=\"DashboardTwitchChatList\"",
            dashboardXaml,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"DashboardTwitchChatList[\s\S]*?Focusable\s*=\s*""False""",
                RegexOptions.CultureInvariant),
            dashboardXaml);
        Assert.Matches(
            new Regex(
                @"DashboardTwitchEventsList[\s\S]{0,400}?Focusable\s*=\s*""False""",
                RegexOptions.CultureInvariant),
            dashboardXaml);
    }

    [Fact]
    public void SchnellzugriffButtons_CenterContent()
    {
        string dashboardXaml = ReadDashboardXaml();

        Assert.Contains(
            "x:Name=\"DashboardPrepareStreamButton\"",
            dashboardXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DashboardPrepareStreamButton\" x:FieldModifier=\"public\" ToolTip=\"Konfigurierte Streaming-Dienste starten und den Stream vorbereiten\" MinHeight=\"48\" MinWidth=\"0\" HorizontalContentAlignment=\"Left\"",
            dashboardXaml,
            StringComparison.Ordinal);

        foreach (string name in new[]
                 {
                     "DashboardPrepareStreamButton",
                     "DashboardObsStartStreamButton",
                     "DashboardObsStopStreamButton",
                     "DashboardQuickAccessAlertButton",
                     "DashboardQuickAccessOverlayButton",
                     "DashboardShortStreamTestButton"
                 })
        {
            Assert.Matches(
                new Regex(
                    $@"{name}[\s\S]*?HorizontalContentAlignment\s*=\s*""Center""",
                    RegexOptions.CultureInvariant),
                dashboardXaml);
        }
    }

    [Fact]
    public void ObsScenePreviewOverlay_UsesLightForeground()
    {
        string dashboardXaml = ReadDashboardXaml();

        Assert.Contains(
            "x:Name=\"DashboardCurrentSceneText\"",
            dashboardXaml,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"DashboardCurrentSceneText[\s\S]*?Foreground\s*=\s*""White""",
                RegexOptions.CultureInvariant),
            dashboardXaml);
    }

    [Fact]
    public void LargeObsScenePreview_UsesFreeSpaceBesidePreviewForActivityPanels()
    {
        string layoutSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Dashboard",
            "MainWindow.Dashboard.Layout.cs"));

        Assert.Contains(
            "MoveDashboardTwitchUsersForLargePreview(useWidePreviewLayout)",
            layoutSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "activityGrid.Children.Add(usersModule)",
            layoutSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Grid.SetRow(usersModule, 1)",
            layoutSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Grid.SetRowSpan(chatModule, 2)",
            layoutSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "Grid.SetRow(DashboardPageViewHost.DashboardPrimaryContentColumn, 0)",
            layoutSource,
            StringComparison.Ordinal);

        string dashboardXaml = ReadDashboardXaml();
        Assert.Contains(
            "x:Name=\"DashboardActivityGrid\"",
            dashboardXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"DashboardActivitySecondaryRow\"",
            dashboardXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChannelEditHint_UsesReadableAccentForeground()
    {
        string dashboardXaml = ReadDashboardXaml();

        Assert.Contains(
            "BEARBEITEN",
            dashboardXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"BEARBEITEN  ›\" Foreground=\"{DynamicResource AccentSelectedForegroundBrush}\"",
            dashboardXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Text=\"BEARBEITEN  ›\" Foreground=\"{DynamicResource AccentHoverBrush}\"",
            dashboardXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LightPastelThemes_HaveReadableMutedTextContrast()
    {
        string themesRoot = Path.Combine(FindRepositoryRoot(), "src", "CreatorControlSuite.App");

        foreach (string themeId in new[] { "vanilla-unicorn-lounge", "pastel-lofi-cafe" })
        {
            ThemeDefinition theme = ThemeCatalog.Resolve(themeId);
            string path = Path.Combine(
                themesRoot,
                theme.ResourcePath.Replace('/', Path.DirectorySeparatorChar));
            XDocument document = XDocument.Load(path);

            string muted = RequireBrushColor(document, "TextMutedBrush");
            string secondary = RequireBrushColor(document, "TextSecondaryBrush");
            string window = RequireBrushColor(document, "WindowBackgroundBrush");
            string highlight = RequireBrushColor(document, "HighlightItemBrush");
            string row = RequireBrushColor(document, "RowBackgroundBrush");

            Assert.True(
                RelativeLuminance(muted) < 0.45,
                $"{themeId}: TextMutedBrush {muted} ist zu hell für Pastell-Hintergründe.");
            Assert.True(
                RelativeLuminance(secondary) < 0.35,
                $"{themeId}: TextSecondaryBrush {secondary} ist zu hell.");
            Assert.True(
                ContrastRatio(muted, window) >= 3.0,
                $"{themeId}: TextMutedBrush auf WindowBackground unterschreitet AA-Large ({ContrastRatio(muted, window):F2}).");
            Assert.True(
                ColorDistance(highlight, row) >= 18,
                $"{themeId}: HighlightItemBrush ist zu nah an RowBackgroundBrush.");
        }
    }

    private static string ReadAppXaml() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "App.xaml"));

    private static string ReadDashboardXaml() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Dashboard",
            "DashboardPageView.xaml"));

    private static string RequireBrushColor(XDocument document, string key)
    {
        XElement? brush = document
            .Descendants()
            .FirstOrDefault(element =>
                (string?)element.Attribute(XamlNs + "Key") == key);
        Assert.NotNull(brush);
        string? color = (string?)brush!.Attribute("Color");
        Assert.False(string.IsNullOrWhiteSpace(color), $"Brush {key} ohne Color-Attribut.");
        return color!;
    }

    private static double RelativeLuminance(string hex)
    {
        (byte r, byte g, byte b) = ParseRgb(hex);
        static double Channel(byte c)
        {
            double s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        double l1 = RelativeLuminance(foregroundHex);
        double l2 = RelativeLuminance(backgroundHex);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double ColorDistance(string a, string b)
    {
        (byte r1, byte g1, byte b1) = ParseRgb(a);
        (byte r2, byte g2, byte b2) = ParseRgb(b);
        return Math.Sqrt(
            ((r1 - r2) * (r1 - r2)) +
            ((g1 - g2) * (g1 - g2)) +
            ((b1 - b2) * (b1 - b2)));
    }

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        string value = hex.Trim();
        if (value.StartsWith('#') && value.Length is 7 or 9)
        {
            int offset = value.Length == 9 ? 3 : 1;
            byte r = byte.Parse(value.AsSpan(offset, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(value.AsSpan(offset + 2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(value.AsSpan(offset + 4, 2), NumberStyles.HexNumber);
            return (r, g, b);
        }

        throw new FormatException($"Ungültige Theme-Farbe: {hex}");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository-Root mit Directory.Build.props wurde nicht gefunden.");
    }
}
