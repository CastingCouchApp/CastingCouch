namespace CreatorControlSuite.Tests;

public sealed class SpotifyServiceUiStructureTests
{
    [Fact]
    public void SpotifyServicePage_UsesFocusedSectionOrderAndHidesLegacyCatalogControls()
    {
        string xaml = File.ReadAllText(FindRepositoryFile(
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Services",
            "SpotifyServiceView.xaml"));

        int player = xaml.IndexOf("Deine Wiedergabe", StringComparison.Ordinal);
        int device = xaml.IndexOf("Wiedergabegerät", StringComparison.Ordinal);
        int automation = xaml.IndexOf("Stream- und Szenenautomatik", StringComparison.Ordinal);
        int overlay = xaml.IndexOf("Overlay-Verhalten", StringComparison.Ordinal);

        Assert.True(player >= 0);
        Assert.True(device > player);
        Assert.True(automation > device);
        Assert.True(overlay > automation);
        Assert.Contains(
            "x:Name=\"SpotifyLegacyCatalogControls\" Visibility=\"Collapsed\"",
            xaml);
        Assert.DoesNotContain("Text=\"Warteschlange\"", xaml);
        Assert.DoesNotContain("Text=\"Zuletzt gespielt\"", xaml);
        Assert.DoesNotContain("Text=\"Gespeicherte Titel", xaml);
    }

    [Fact]
    public void SpotifyServicePage_PresentsSceneMusicAsPrimaryAction()
    {
        string xaml = File.ReadAllText(FindRepositoryFile(
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Services",
            "SpotifyServiceView.xaml"));

        Assert.Contains("x:Name=\"ServicesSpotifyEditSceneMusicButton\"", xaml);
        Assert.Contains("Content=\"MUSIK PRO OBS-SZENE EINRICHTEN\"", xaml);
        Assert.Contains("Playlist, Shuffle, Lautstärke und Fade", xaml);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
