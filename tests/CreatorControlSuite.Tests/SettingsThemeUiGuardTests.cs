namespace CreatorControlSuite.Tests;

public sealed class SettingsThemeUiGuardTests
{
    [Fact]
    public void AppComboBoxTemplate_SupportsEditableTextDisplay()
    {
        string comboStyle = ExtractStyleBlock(
            ReadAppXaml(),
            "TargetType=\"ComboBox\"",
            "TargetType=\"ComboBoxItem\"");

        Assert.Contains(
            "x:Name=\"PART_EditableTextBox\"",
            comboStyle,
            StringComparison.Ordinal);
        Assert.Contains(
            "Property=\"IsEditable\" Value=\"True\"",
            comboStyle,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource TextPrimaryBrush",
            comboStyle,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppTabControlTemplate_AllowsHorizontalHeaderScroll()
    {
        string tabControlStyle = ExtractStyleBlock(
            ReadAppXaml(),
            "TargetType=\"TabControl\"",
            "TargetType=\"ListBox\"");

        Assert.Contains(
            "HorizontalScrollBarVisibility=\"Auto\"",
            tabControlStyle,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsItemsHost=\"True\"",
            tabControlStyle,
            StringComparison.Ordinal);
        Assert.Contains(
            "PART_SelectedContentHost",
            tabControlStyle,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppSeparatorStyle_UsesThemeBorderBrush()
    {
        string appXaml = ReadAppXaml();

        Assert.Contains(
            "TargetType=\"Separator\"",
            appXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource StrongBorderBrush",
            appXaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralSettingsView_UsesThemeForegroundTokens()
    {
        string xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Settings",
            "GeneralSettingsView.xaml"));

        Assert.Contains(
            "DynamicResource AccentBrush",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "DynamicResource TextMutedBrush",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Foreground=\"#",
            xaml,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Background=\"#",
            xaml,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StreamWorkflowSettings_UsesThemeLabelsAndEditableCombos()
    {
        string xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Settings",
            "SettingsPageView.xaml"));

        int workflowStart = xaml.IndexOf(
            "Header=\"Stream-Workflow\"",
            StringComparison.Ordinal);
        Assert.True(workflowStart >= 0);

        int workflowEnd = xaml.IndexOf(
            "Header=\"Stream Deck\"",
            workflowStart,
            StringComparison.Ordinal);
        Assert.True(workflowEnd > workflowStart);
        string workflowBlock = xaml[workflowStart..workflowEnd];

        Assert.Contains(
            "CCSDarkSettingsContentStyle",
            workflowBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "Foreground=\"{DynamicResource AccentBrush}\"",
            workflowBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"StartSceneBox\"",
            workflowBlock,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsEditable=\"True\"",
            workflowBlock,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Foreground=\"#",
            workflowBlock,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadAppXaml() =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "App.xaml"));

    private static string ExtractStyleBlock(
        string appXaml,
        string startMarker,
        string endMarker)
    {
        int start = appXaml.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Style-Start fehlt: {startMarker}");
        string fromStart = appXaml[start..];
        int end = fromStart.IndexOf(endMarker, StringComparison.Ordinal);
        Assert.True(end > 0, $"Style-Ende fehlt: {endMarker}");
        return fromStart[..end];
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
