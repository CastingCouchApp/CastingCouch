namespace CreatorControlSuite.Tests;

public sealed class ApplicationIconTests
{
    [Fact]
    public void AppAndMainWindow_UseCastingCouchMultiSizeIcon()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "CreatorControlSuite.App.csproj"));
        string mainWindow = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));
        string iconPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Assets",
            "Brand",
            "castingcouch-app.ico");

        Assert.Contains(
            @"<ApplicationIcon>Assets\Brand\castingcouch-app.ico</ApplicationIcon>",
            project,
            StringComparison.Ordinal);
        Assert.Contains(
            "Icon=\"pack://application:,,,/Assets/Brand/castingcouch-app.ico\"",
            mainWindow,
            StringComparison.Ordinal);

        byte[] icon = File.ReadAllBytes(iconPath);
        Assert.Equal(0, BitConverter.ToUInt16(icon, 0));
        Assert.Equal(1, BitConverter.ToUInt16(icon, 2));
        Assert.True(BitConverter.ToUInt16(icon, 4) >= 6);
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

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
