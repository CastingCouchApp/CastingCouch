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
        Assert.Contains("x:Name=\"ServicesSpotifyShuffleBox\"", xaml);
        Assert.Contains("Content=\"Stream- und Szenenautomatik aktivieren\"", xaml);
        Assert.Contains("x:Name=\"SpotifyAutomationContent\"", xaml);
        Assert.Contains("x:Name=\"ServicesSpotifySaveAutomationButton\"", xaml);
        Assert.Contains("Content=\"AUTOMATIK-EINSTELLUNGEN SPEICHERN\"", xaml);
        Assert.Contains("Binding=\"{Binding IsChecked, ElementName=ServicesSpotifySmartAutomationBox}\"", xaml);
        Assert.Contains("Text=\"Overlay-Verhalten\"", xaml);
        Assert.Contains("Text=\"Musik erkennen über\"", xaml);
        Assert.DoesNotContain("x:Name=\"ServicesSpotifyPauseButton\"", xaml);
        Assert.DoesNotContain("Text=\"Deaktiviert\"", xaml);
        Assert.DoesNotContain("Content=\"ALERT-EINSTELLUNGEN SPEICHERN\"", xaml);
        Assert.DoesNotContain("Diagnose und Protokoll", xaml);
        Assert.DoesNotContain("BorderBrush=\"{DynamicResource AccentBrush}\"", xaml);

        string alertXaml = File.ReadAllText(FindRepositoryFile(
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Spotify",
            "SpotifyAutomationView.xaml"));
        Assert.DoesNotContain("AutoSave_OnChanged", alertXaml);
        Assert.DoesNotContain("SaveCommand", alertXaml);
    }

    [Fact]
    public void SpotifyAutomationRuntime_UsesMasterSwitchForScenesAlertsAndOverlay()
    {
        string sceneRuntime = ReadSpotifyShellFile("MainWindow.Services.Spotify.CatalogDevices.cs");
        string alertRuntime = ReadSpotifyShellFile("MainWindow.Services.Spotify.ConnectionAutomation.cs");
        string overlayRuntime = ReadSpotifyShellFile("MainWindow.Services.Spotify.Visibility.cs");

        Assert.Contains("!_settings.Spotify.SmartAutomationEnabled", sceneRuntime);
        Assert.Contains("!_settings.Spotify.SmartAutomationEnabled", alertRuntime);
        Assert.Contains("!_settings.Spotify.SmartAutomationEnabled", overlayRuntime);

        string bindings = ReadSpotifyShellFile("MainWindow.Services.Spotify.Bindings.cs");
        Assert.Contains("ServicesSpotifySmartAutomationBox.Checked +=", bindings);
        Assert.Contains("ServicesSpotifySmartAutomationBox.Unchecked +=", bindings);
        Assert.Contains("SaveSpotifySmartAutomationSettingsAsync", bindings);
    }

    private static string ReadSpotifyShellFile(string fileName) =>
        File.ReadAllText(FindRepositoryFile(
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Spotify",
            fileName));

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
