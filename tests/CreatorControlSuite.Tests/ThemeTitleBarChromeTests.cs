using System.Xml.Linq;
using CreatorControlSuite.App.Themes;

namespace CreatorControlSuite.Tests;

public sealed class ThemeTitleBarChromeTests
{
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void AllThemes_DefineTitleBarChromeTokens()
    {
        string themesRoot = Path.Combine(FindRepositoryRoot(), "src", "CreatorControlSuite.App");
        var missing = new List<string>();

        foreach (ThemeDefinition theme in ThemeCatalog.All)
        {
            string path = Path.Combine(
                themesRoot,
                theme.ResourcePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Theme-Datei fehlt: {theme.ResourcePath}");

            XDocument document = XDocument.Load(path);
            HashSet<string> keys = document
                .Descendants()
                .Select(element => (string?)element.Attribute(XamlNs + "Key"))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key!)
                .ToHashSet(StringComparer.Ordinal);

            foreach (string required in new[]
                     {
                         "TitleBarBackgroundBrush",
                         "TitleBarHighlightBrush",
                         "TitleBarDividerBrush"
                     })
            {
                if (!keys.Contains(required))
                {
                    missing.Add($"{theme.Id}: {required}");
                }
            }

            XElement? background = document
                .Descendants()
                .FirstOrDefault(element =>
                    (string?)element.Attribute(XamlNs + "Key") == "TitleBarBackgroundBrush");
            if (background is null
                || !background.Name.LocalName.Contains("Gradient", StringComparison.Ordinal))
            {
                missing.Add($"{theme.Id}: TitleBarBackgroundBrush muss ein GradientBrush sein");
            }
        }

        Assert.True(
            missing.Count == 0,
            "TitleBar-Chrome-Tokens fehlen:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void MainWindowTitleBar_UsesChromeTokensWithoutWidgetCards()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml");
        string xaml = File.ReadAllText(path);

        Assert.Contains(
            "Background=\"{DynamicResource TitleBarBackgroundBrush}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Background=\"{DynamicResource TitleBarHighlightBrush}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Style=\"{StaticResource TitleBarWidgetStyle}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Style=\"{StaticResource TitleBarDividerStyle}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Tag=\"TitleBarChromeWidget\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Tag=\"TitleBarChromeDivider\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "MouseRightButtonUp=\"TitleBar_MouseRightButtonUp\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Uid=\"Stream\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Margin=\"0,0,0,0\"",
            xaml,
            StringComparison.Ordinal);

        string appXaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "App.xaml"));
        Assert.Contains(
            "x:Key=\"TitleBarWidgetCardStyle\"",
            appXaml,
            StringComparison.Ordinal);

        int serviceSectionStart = xaml.IndexOf(
            "x:Name=\"DashboardServiceStatusSection\"",
            StringComparison.Ordinal);
        int serviceSectionEnd = xaml.IndexOf(
            "<!-- Kompatibilitäts-Host für Status/Connect-Controls -->",
            StringComparison.Ordinal);
        Assert.True(serviceSectionStart >= 0 && serviceSectionEnd > serviceSectionStart);

        string widgetStrip = xaml[serviceSectionStart..serviceSectionEnd];
        Assert.DoesNotContain(
            "Background=\"{DynamicResource CardBackgroundBrush}\"",
            widgetStrip,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CornerRadius=\"9\"",
            widgetStrip,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowTitleBar_UsesCompactBrandWidgetsAndConnectionSummary()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml");
        string xaml = File.ReadAllText(path);

        Assert.DoesNotContain(
            "Text=\"STREAMING CONTROL SUITE\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"TitleBarBrandPanel\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Margin=\"0,0,0,0\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"DashboardHeaderStreamActionButton\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Padding=\"10,6\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"DashboardConnectionSummaryChip\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Padding=\"8,5\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowTitleBar_CentersWordmarkAndConnectionsBeforeStreamWidget()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml");
        string xaml = File.ReadAllText(path);

        Assert.Contains(
            "<ColumnDefinition Width=\"340\"/>",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Source=\"Assets/Brand/castingcouch-horizontal-logo.png\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Width=\"300\"",
            xaml,
            StringComparison.Ordinal);

        int brandPanel = xaml.IndexOf(
            "x:Name=\"TitleBarBrandPanel\"",
            StringComparison.Ordinal);
        int connections = xaml.IndexOf(
            "x:Name=\"DashboardConnectionSummaryChip\"",
            StringComparison.Ordinal);
        int streamWidgets = xaml.IndexOf(
            "x:Name=\"DashboardTopStatusRow\"",
            StringComparison.Ordinal);

        Assert.True(brandPanel >= 0);
        Assert.True(connections > brandPanel);
        Assert.True(streamWidgets > connections);
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
