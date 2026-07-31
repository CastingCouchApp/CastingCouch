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

        Assert.Contains("MaxWidth=\"1380\"", xaml);
        Assert.Contains("ServicesOpenTwitchStatisticsButton", xaml);
        Assert.Contains("ServicesOpenTwitchIntelligenceButton", xaml);
        Assert.DoesNotContain("Header=\"STREAM-STATISTIKEN ANZEIGEN\"", xaml);
        Assert.DoesNotContain("Header=\"INTELLIGENCE-ANALYSE", xaml);
        Assert.True(
            xaml.IndexOf("Text=\"Steuerung\"", StringComparison.Ordinal) <
            xaml.IndexOf("Twitch Professional · Live-Analyse", StringComparison.Ordinal));
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
        Assert.True(
            xaml.LastIndexOf("ServicesTwitchSaveEndSettingsButton", StringComparison.Ordinal) >
            xaml.LastIndexOf("Streamende und Raid", StringComparison.Ordinal));
        Assert.Contains("Text=\"Streamziele\" FontSize=\"18\"", xaml);
        Assert.Contains("Text=\"Streamziele\" FontSize=\"18\" FontWeight=\"SemiBold\" Foreground=\"{DynamicResource TextPrimaryBrush}\"", xaml);
        Assert.Contains("Text=\"Chat und Moderation\"", xaml);
        Assert.Contains("Text=\"Steuerung\" FontWeight=\"SemiBold\" FontSize=\"18\"", xaml);
        Assert.True(
            xaml.IndexOf("x:Name=\"ServicesTwitchTitleBox\"", StringComparison.Ordinal) <
            xaml.IndexOf("Text=\"Chat und Moderation\"", StringComparison.Ordinal));
        Assert.Contains("ServicesOpenTwitchPollButton", xaml);
        Assert.Contains("ServicesOpenTwitchPredictionButton", xaml);
        Assert.Contains("ServicesOpenTwitchChannelPointsButton", xaml);
        Assert.DoesNotContain("ServicesTwitchEndFollowerGoalTargetBox", xaml);

        string goalsXaml = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Twitch",
            "TwitchGoalsView.xaml"));
        Assert.Contains("TextElement.Foreground=\"{DynamicResource TextPrimaryBrush}\"", goalsXaml);

        string engagement = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.Engagement.cs"));
        Assert.DoesNotContain("_twitchGoalsPageViewModel.FollowerTarget =", engagement);
    }

    [Fact]
    public void TwitchService_PreservesEditedChannelFieldsAndShowsCategoryDropdown()
    {
        string viewPath = Path.Combine(
            RepositoryRoot,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Services",
            "TwitchServiceView.xaml");
        string codeBehind = File.ReadAllText(viewPath + ".cs");
        string xaml = File.ReadAllText(viewPath);

        Assert.Contains("IsChannelEditorDirty", codeBehind);
        Assert.Contains("RefreshChannelEditor", codeBehind);
        Assert.Contains("MarkChannelEditorSaved", codeBehind);
        Assert.Contains("MaxDropDownHeight=\"360\"", xaml);
    }

    [Fact]
    public void DashboardLiveRefresh_ReloadsExternallyChangedChannelMetadata()
    {
        string moduleCode = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CreatorControlSuite.Modules.Twitch",
            "TwitchModule.cs"));
        string dashboardCode = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Dashboard",
            "MainWindow.Dashboard.LiveData.cs"));

        Assert.Contains("RefreshChannelInformationAsync", moduleCode);
        int metadataRefresh = dashboardCode.IndexOf(
            "await _twitchModule.RefreshChannelInformationAsync()",
            StringComparison.Ordinal);
        int uiRefresh = dashboardCode.IndexOf(
            "RefreshTwitchUi();",
            metadataRefresh,
            StringComparison.Ordinal);

        Assert.True(metadataRefresh >= 0);
        Assert.True(uiRefresh > metadataRefresh);
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
