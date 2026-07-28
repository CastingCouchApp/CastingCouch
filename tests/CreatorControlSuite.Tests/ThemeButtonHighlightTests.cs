using System.Xml.Linq;
using CreatorControlSuite.App.Themes;

namespace CreatorControlSuite.Tests;

public sealed class ThemeButtonHighlightTests
{
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void AllThemes_DefineButtonHighlightTokens()
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
                         "ButtonHighlightBrush",
                         "ButtonPressedBrush"
                     })
            {
                if (!keys.Contains(required))
                {
                    missing.Add($"{theme.Id}: {required}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Button-Highlight-Tokens fehlen:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void AppButtonStyle_UsesThemedHighlightTemplate()
    {
        string appXaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "App.xaml"));

        Assert.Contains(
            "TargetType=\"Button\"",
            appXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource ButtonHighlightBrush",
            appXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource ButtonPressedBrush",
            appXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "ControlTemplate TargetType=\"Button\"",
            appXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Property=\"IsMouseOver\" Value=\"True\"",
            appXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Property=\"IsPressed\" Value=\"True\"",
            appXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarNavButtonStyle_UsesThemedHighlightAndActiveTag()
    {
        string appXaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "App.xaml"));

        int styleStart = appXaml.IndexOf(
            "x:Key=\"SidebarNavButtonStyle\"",
            StringComparison.Ordinal);
        Assert.True(styleStart >= 0);
        string styleBlock = appXaml[styleStart..];
        int nextStyle = styleBlock.IndexOf(
            "x:Key=\"DashboardCardStyle\"",
            StringComparison.Ordinal);
        if (nextStyle > 0)
        {
            styleBlock = styleBlock[..nextStyle];
        }

        Assert.Contains(
            "DynamicResource ButtonHighlightBrush",
            styleBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource ButtonPressedBrush",
            styleBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource NavActiveBackgroundBrush",
            styleBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource NavActiveForegroundBrush",
            styleBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "Value=\"Active\"",
            styleBlock,
            StringComparison.Ordinal);

        string navigationCs = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Navigation",
            "MainWindow.Navigation.cs"));
        Assert.Contains(
            "Tag = \"Active\"",
            navigationCs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "GetBrush(\"NavActiveBackgroundBrush\")",
            navigationCs,
            StringComparison.Ordinal);
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
