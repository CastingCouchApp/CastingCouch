using System.Xml.Linq;

namespace CreatorControlSuite.Tests;

public sealed class ArchitectureGuardTests
{
    private static readonly IReadOnlyDictionary<string, int> OversizedFileBaseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/CreatorControlSuite.App/Shell/MainWindow.xaml.cs"] = 495
        };

    [Fact]
    [Trait("Category", "Architecture")]
    public void SourceFiles_DoNotIntroduceOrGrowOversizedDebt()
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (string path in Directory.EnumerateFiles(
                     sourceRoot,
                     "*",
                     SearchOption.AllDirectories)
                 .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            int lineCount = File.ReadLines(path).Count();
            if (OversizedFileBaseline.TryGetValue(relative, out int baseline))
            {
                if (lineCount > baseline)
                {
                    violations.Add($"{relative}: {lineCount} > Baseline {baseline}");
                }

                continue;
            }

            if (lineCount >= 1_000)
            {
                violations.Add($"{relative}: neue übergroße Datei mit {lineCount} Zeilen");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Architekturgrößen-Gate verletzt:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void OverlayTypeScriptFiles_RemainBelowOneThousandLines()
    {
        string root = FindRepositoryRoot();
        string overlaySourceRoot = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.Modules.Overlay",
            "CanvasOverlay",
            "src");
        string[] violations = Directory
            .EnumerateFiles(overlaySourceRoot, "*.ts", SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Count() >= 1_000)
            .Select(path =>
                $"{Path.GetRelativePath(root, path).Replace('\\', '/')}: " +
                $"{File.ReadLines(path).Count()} Zeilen")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Overlay-TypeScript-Dateien müssen unter 1.000 Zeilen bleiben:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void CoreAndModules_DoNotReferenceApp()
    {
        string root = FindRepositoryRoot();
        string[] projectFiles = Directory.GetFiles(
            Path.Combine(root, "src"),
            "*.csproj",
            SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (string projectFile in projectFiles)
        {
            string projectName = Path.GetFileNameWithoutExtension(projectFile);
            if (projectName == "CreatorControlSuite.App")
            {
                continue;
            }

            XDocument project = XDocument.Load(projectFile);
            IEnumerable<string> references = project
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? "");
            if (references.Any(reference =>
                    reference.Contains(
                        "CreatorControlSuite.App",
                        StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add(projectName + " → CreatorControlSuite.App");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Unerlaubte Rückreferenzen:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Core_HasNoProjectReferences()
    {
        string root = FindRepositoryRoot();
        string projectPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.Core",
            "CreatorControlSuite.Core.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.Empty(project.Descendants("ProjectReference"));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ModuleDependencies_MatchExplicitAllowlist()
    {
        string root = FindRepositoryRoot();
        var allowed = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["CreatorControlSuite.Modules.OBS"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.Twitch"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.Spotify"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.Alerts"] =
                ["CreatorControlSuite.Core", "CreatorControlSuite.Modules.OBS"],
            ["CreatorControlSuite.Modules.Overlay"] =
                ["CreatorControlSuite.Core", "CreatorControlSuite.Modules.OBS"],
            ["CreatorControlSuite.Modules.Workflow"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.StreamDeck"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.YouTubeMusic"] = ["CreatorControlSuite.Core"]
        };
        var violations = new List<string>();
        foreach ((string projectName, string[] allowedReferences) in allowed)
        {
            string projectPath = Path.Combine(
                root,
                "src",
                projectName,
                projectName + ".csproj");
            XDocument project = XDocument.Load(projectPath);
            string[] actual = [.. project.Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? "")
                .Select(value => Path.GetFileNameWithoutExtension(value) ?? "")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
            string[] expected = [.. allowedReferences
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
            if (!actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            {
                violations.Add(
                    $"{projectName}: [{string.Join(", ", actual)}], erwartet " +
                    $"[{string.Join(", ", expected)}]");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Unerlaubte Modulabhängigkeiten:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void CommercialLicensing_IsNotPartOfProduct()
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src");
        string licensingRoot = Path.Combine(
            sourceRoot,
            "CreatorControlSuite.Core",
            "Licensing");
        string[] licensingSources = Directory.Exists(licensingRoot)
            ? Directory.GetFiles(licensingRoot, "*.cs", SearchOption.AllDirectories)
            : [];

        Assert.Empty(licensingSources);
        Assert.False(
            File.Exists(Path.Combine(
                sourceRoot,
                "CreatorControlSuite.LicenseMockServer",
                "CreatorControlSuite.LicenseMockServer.csproj")),
            "Der Lizenz-Mockserver darf nicht wieder eingeführt werden.");

        string[] prohibitedTokens =
        [
            "CreatorControlSuite.Core.Licensing",
            "IFeatureGate",
            "ILicenseService",
            "LicenseMockServer",
            "license-public.pem"
        ];
        string[] sourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var violations = new List<string>();

        foreach (string sourceFile in sourceFiles)
        {
            string content = File.ReadAllText(sourceFile);
            foreach (string token in prohibitedTokens.Where(token =>
                         content.Contains(token, StringComparison.Ordinal)))
            {
                violations.Add(
                    $"{Path.GetRelativePath(root, sourceFile)}: {token}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Kommerzielle Laufzeit-Lizenzierung gefunden:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void MusicPlayerPage_IsEncapsulatedBehindItsViewApi()
    {
        string root = FindRepositoryRoot();
        string mainWindowCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml.cs"));
        string mainWindowXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));
        string[] leakedControlAccesses =
        [
            ".MusicPlayerPreviousButton",
            ".MusicPlayerPlayPauseButton",
            ".MusicPlayerNextButton",
            ".MusicPlayerConnectButton",
            ".MusicPlayerDisconnectButton",
            ".MusicPlayerProgressBar",
            ".MusicPlayerVolumeSlider",
            ".MusicPlayerBookmarkletBox",
            ".MusicPlayerBookmarkletDragChip",
            ".MusicPlayerBridgeStatusText",
            ".MusicPlayerCoverImage"
        ];

        Assert.Contains(
            "<music:MusicPlayerPageView x:Name=\"MusicPlayerPageViewHost\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"MusicPlayerPreviousButton\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        foreach (string controlAccess in leakedControlAccesses)
        {
            Assert.DoesNotContain(
                controlAccess,
                mainWindowCode,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void WorkflowPage_IsSplitIntoBoundedViews()
    {
        string root = FindRepositoryRoot();
        string mainWindowCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml.cs"));
        string mainWindowXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));
        string workflowPageXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Workflow",
            "WorkflowPageView.xaml"));

        Assert.Contains(
            "<workflow:WorkflowPageView x:Name=\"WorkflowPageViewHost\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"RunOfShowPlanBox\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains("<workflow:RunOfShowView", workflowPageXaml);
        Assert.Contains("<workflow:TimedAutomationView", workflowPageXaml);
        Assert.Contains("<workflow:WorkflowDesignerView", workflowPageXaml);
        Assert.Contains("<workflow:ShortStreamTestView", workflowPageXaml);

        string[] leakedPageControls =
        [
            ".PrepareStreamButton",
            ".StartCountdownButton",
            ".StopCountdownButton",
            ".GoLiveButton",
            ".PauseStreamButton",
            ".ResumeStreamButton",
            ".EndStreamButton",
            ".WorkflowStatusText",
            ".WorkflowTabControl"
        ];
        foreach (string controlAccess in leakedPageControls)
        {
            Assert.DoesNotContain(
                controlAccess,
                mainWindowCode,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void SettingsPage_IsEncapsulatedBehindItsViewApi()
    {
        string root = FindRepositoryRoot();
        string mainWindowCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml.cs"));
        string mainWindowXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));
        string settingsPageXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Settings",
            "SettingsPageView.xaml"));

        Assert.Contains(
            "<settings:SettingsPageView x:Name=\"SettingsPageViewHost\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"ObsHostBox\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains("<settings:GeneralSettingsView", settingsPageXaml);
        Assert.Contains("<legal:LegalSettingsView", settingsPageXaml);
        Assert.Contains("<updates:UpdateSettingsView", settingsPageXaml);
        Assert.Contains("<migration:MigrationSettingsView", settingsPageXaml);
        Assert.DoesNotContain(
            "ElementName=AlertRuntimeViewHost",
            settingsPageXaml,
            StringComparison.Ordinal);

        string[] leakedPageControls =
        [
            ".SettingsTabControl",
            ".SettingsStatusText",
            ".SaveSettingsButton"
        ];
        foreach (string controlAccess in leakedPageControls)
        {
            Assert.DoesNotContain(
                controlAccess,
                mainWindowCode,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ServicesPage_IsSplitByServiceBoundary()
    {
        string root = FindRepositoryRoot();
        string mainWindowCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml.cs"));
        string mainWindowXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));
        string servicesPageXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Services",
            "ServicesPageView.xaml"));

        Assert.Contains(
            "<services:ServicesPageView x:Name=\"ServicesPageViewHost\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"ServicesSpotifyNowPlayingText\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains("<services:SpotifyServiceView", servicesPageXaml);
        Assert.Contains("<services:TwitchServiceView", servicesPageXaml);
        Assert.Contains("<services:ObsServiceView", servicesPageXaml);
        Assert.Contains("<services:StreamerBotServiceView", servicesPageXaml);
        Assert.Contains("<services:StreamDeckServiceView", servicesPageXaml);

        string[] leakedPageControls =
        [
            ".ServicesOverviewPanel",
            ".ServicesTabControl",
            ".ServicesOverviewSpotifyButton",
            ".ServicesOverviewTwitchButton",
            ".ServicesOverviewObsButton",
            ".ServicesOverviewStreamerBotButton",
            ".ServicesOverviewStreamDeckButton"
        ];
        foreach (string controlAccess in leakedPageControls)
        {
            Assert.DoesNotContain(
                controlAccess,
                mainWindowCode,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ServiceShellLogic_IsSplitIntoBoundedPartials()
    {
        string root = FindRepositoryRoot();
        string shellRoot = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell");
        string mainWindowCode = File.ReadAllText(Path.Combine(
            shellRoot,
            "MainWindow.xaml.cs"));
        string[] extractedMethodDefinitions =
        [
            "private async Task AuthorizeSpotifyAsync()",
            "private async Task AuthorizeTwitchAsync()",
            "private async Task ConnectObsAsync(bool showErrorDialog = true)",
            "private async Task StartObsStreamAsync()",
            "private string GetSpotifyAutomationEditorGroup()",
            "private async Task ConnectStreamerBotAsync()",
            "private async Task RunDiagnosticsAsync()",
            "private async Task RefreshCreatorIntelligenceAsync()",
            "private async Task EvaluateTimedAutomationRulesAsync()",
            "private bool IsSpotifyMusicProvider()",
            "private string ResolveActiveOverlayDataPath()",
            "private void InitializeDashboardBindings()",
            "private void InitializeServiceBindings()",
            "private void InitializeTimedAutomationBindings()"
        ];

        foreach (string methodDefinition in extractedMethodDefinitions)
        {
            Assert.DoesNotContain(
                methodDefinition,
                mainWindowCode,
                StringComparison.Ordinal);
        }

        string[] serviceDirectories =
            ["Spotify", "Twitch", "Obs", "StreamerBot", "CreatorIntelligence"];
        foreach (string serviceDirectory in serviceDirectories)
        {
            string directory = Path.Combine(
                shellRoot,
                "Services",
                serviceDirectory);
            string[] partials = Directory.GetFiles(
                directory,
                "MainWindow.Services.*.cs",
                SearchOption.TopDirectoryOnly);

            Assert.NotEmpty(partials);
            Assert.All(
                partials,
                path => Assert.True(
                    File.ReadLines(path).Count() < 1_000,
                    $"{Path.GetFileName(path)} muss unter 1.000 Zeilen bleiben."));
        }

        string[] shellSliceDirectories =
        [
            "Alerts",
            "Dashboard",
            "Diagnostics",
            "Initialization",
            "Lifecycle",
            "MultiPc",
            "Music",
            "Navigation",
            "Overlay",
            "Workflow"
        ];
        foreach (string shellSliceDirectory in shellSliceDirectories)
        {
            string directory = Path.Combine(shellRoot, shellSliceDirectory);
            string[] partials = Directory.GetFiles(
                directory,
                "MainWindow.*.cs",
                SearchOption.TopDirectoryOnly);

            Assert.NotEmpty(partials);
            Assert.All(
                partials,
                path => Assert.True(
                    File.ReadLines(path).Count() < 1_000,
                    $"{Path.GetFileName(path)} muss unter 1.000 Zeilen bleiben."));
        }

        Assert.True(
            File.ReadLines(Path.Combine(shellRoot, "MainWindow.xaml.cs")).Count()
            < 500,
            "MainWindow.xaml.cs muss dauerhaft unter 500 Zeilen bleiben.");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void DashboardPage_IsHostedOutsideMainWindowXaml()
    {
        string root = FindRepositoryRoot();
        string mainWindowXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));
        string dashboardPageXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Views",
            "Pages",
            "Dashboard",
            "DashboardPageView.xaml"));

        Assert.Contains(
            "<dashboard:DashboardPageView x:Name=\"DashboardPageViewHost\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "x:Name=\"DashboardStreamControlModule\"",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"DashboardStreamControlModule\"",
            dashboardPageXaml,
            StringComparison.Ordinal);
        Assert.True(
            File.ReadLines(Path.Combine(
                root,
                "src",
                "CreatorControlSuite.App",
                "Shell",
                "MainWindow.xaml")).Count() < 1_000,
            "MainWindow.xaml muss dauerhaft unter 1.000 Zeilen bleiben.");
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
