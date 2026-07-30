namespace CreatorControlSuite.Tests;

public sealed class NavigationSelectionTests
{
    [Fact]
    public void SetActiveNavigationButton_ReplacesThePreviousActiveTag()
    {
        string navigationSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Navigation",
            "MainWindow.Navigation.cs"));

        Assert.Contains("MultiPcButton,", navigationSource, StringComparison.Ordinal);
        Assert.Contains(
            "button.Tag = null;",
            navigationSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "button.ClearValue(FrameworkElement.TagProperty);",
            navigationSource,
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
