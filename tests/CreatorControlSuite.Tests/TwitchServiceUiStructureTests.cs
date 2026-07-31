namespace CreatorControlSuite.Tests;

public sealed class TwitchServiceUiStructureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void TwitchService_UsesCompactLayoutAndPopoutAnalysisWindows()
    {
        string xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Services",
            "TwitchServiceView.xaml"));

        Assert.Contains("MaxWidth=\"1120\"", xaml);
        Assert.Contains("ServicesOpenTwitchStatisticsButton", xaml);
        Assert.Contains("ServicesOpenTwitchIntelligenceButton", xaml);
        Assert.DoesNotContain("Header=\"STREAM-STATISTIKEN ANZEIGEN\"", xaml);
        Assert.DoesNotContain("Header=\"INTELLIGENCE-ANALYSE", xaml);
    }

    [Fact]
    public void TwitchService_OrdersLiveColumnsAndUsesOptionalRaidAutomation()
    {
        string xaml = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Services",
            "TwitchServiceView.xaml"));

        int events = xaml.IndexOf("x:Name=\"ServicesTwitchEventsList\"", StringComparison.Ordinal);
        int chat = xaml.IndexOf("x:Name=\"ServicesTwitchChatList\"", StringComparison.Ordinal);
        int users = xaml.IndexOf("x:Name=\"ServicesTwitchUsersList\"", StringComparison.Ordinal);

        Assert.True(events < chat && chat < users);
        Assert.Contains("ServicesTwitchRaidEnabledBox", xaml);
        Assert.Contains("ServicesTwitchRaidAutomationPanel", xaml);
        Assert.Contains("ALLE TWITCH-EINSTELLUNGEN SPEICHERN", xaml);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CreatorControlSuite.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    }
}
