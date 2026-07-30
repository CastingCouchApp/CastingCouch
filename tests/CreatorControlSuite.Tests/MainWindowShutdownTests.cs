namespace CreatorControlSuite.Tests;

public sealed class MainWindowShutdownTests
{
    [Fact]
    public void MainWindowClose_IsNotCoupledToObsStreamEnd()
    {
        string root = FindRepositoryRoot();
        string shellRoot = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell");
        string source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(shellRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("OnMainWindowClosing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_closeAfterStreamEnd", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_allowMainWindowClose", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCloseApplicationAfterStreamEnd", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
