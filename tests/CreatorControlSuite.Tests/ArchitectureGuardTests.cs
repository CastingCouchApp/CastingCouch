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
                 .Where(IsTrackedSourcePath)
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
    public void AgentProgram_ComposesObsEndpointsWithoutImplementingThem()
    {
        string root = FindRepositoryRoot();
        string agentRoot = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.Agent");
        string programPath = Path.Combine(agentRoot, "Program.cs");
        string program = File.ReadAllText(programPath);
        string endpointsPath = Path.Combine(
            agentRoot,
            "Endpoints",
            "ObsEndpointMappings.cs");

        Assert.True(
            File.ReadLines(programPath).Count() < 600,
            "Agent/Program.cs muss als Composition Root unter 600 Zeilen bleiben.");
        Assert.Contains(
            "app.MapObsEndpoints(",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"/api/v1/obs/",
            program,
            StringComparison.Ordinal);
        Assert.True(
            File.Exists(endpointsPath),
            "OBS-Endpunkte müssen in einer eigenen Mapping-Datei liegen.");
        Assert.True(
            File.ReadLines(endpointsPath).Count() < 1_000,
            "OBS-Endpunkt-Mapping muss unter 1.000 Zeilen bleiben.");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AgentProgram_ComposesUpdateEndpointsWithoutImplementingThem()
    {
        string root = FindRepositoryRoot();
        string agentRoot = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.Agent");
        string programPath = Path.Combine(agentRoot, "Program.cs");
        string program = File.ReadAllText(programPath);
        string endpointsPath = Path.Combine(
            agentRoot,
            "Endpoints",
            "UpdateEndpointMappings.cs");

        Assert.True(
            File.ReadLines(programPath).Count() < 325,
            "Agent/Program.cs muss nach der Update-Extraktion unter 325 Zeilen bleiben.");
        Assert.Contains(
            "app.MapUpdateEndpoints(",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"/api/v1/update/",
            program,
            StringComparison.Ordinal);
        Assert.True(
            File.Exists(endpointsPath),
            "Update-Endpunkte müssen in einer eigenen Mapping-Datei liegen.");
        Assert.True(
            File.ReadLines(endpointsPath).Count() < 1_000,
            "Update-Endpunkt-Mapping muss unter 1.000 Zeilen bleiben.");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AgentProgram_OnlyComposesEndpointGroups()
    {
        string root = FindRepositoryRoot();
        string programPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.Agent",
            "Program.cs");
        string program = File.ReadAllText(programPath);

        Assert.True(
            File.ReadLines(programPath).Count() < 160,
            "Agent/Program.cs muss als reiner Composition Root unter 160 Zeilen bleiben.");
        Assert.Contains(
            "app.MapOperationsEndpoints(",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "app.MapSecurityEndpoints(",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain("app.MapGet(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("app.MapPost(", program, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void AgentEndpoints_UseCentralProblemDetailsFactory()
    {
        string root = FindRepositoryRoot();
        string endpointsRoot = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.Agent",
            "Endpoints");
        string[] prohibited =
        [
            "return Results.BadRequest(",
            "return Results.Unauthorized(",
            "return Results.NotFound(",
            "return Results.StatusCode(",
            "return Results.Problem("
        ];
        var violations = new List<string>();

        foreach (string path in Directory.EnumerateFiles(
                     endpointsRoot,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            foreach (string token in prohibited)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetFileName(path)}: {token}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void RunOfShowShell_DelegatesPlanRulesToApplicationService()
    {
        string root = FindRepositoryRoot();
        string shellPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Workflow",
            "MainWindow.Workflow.RunOfShow.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "RunOfShowPlanService.cs");
        string runtimePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Workflow",
            "MainWindow.Workflow.RunOfShowRuntime.cs");
        string shell = File.ReadAllText(shellPath);
        string runtime = File.ReadAllText(runtimePath);

        Assert.True(
            File.ReadLines(shellPath).Count() < 650,
            "Die Run-of-Show-Plan-/Editor-Shell muss unter 650 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(runtimePath).Count() < 400,
            "Die Run-of-Show-Runtime-Shell muss unter 400 Zeilen bleiben.");
        Assert.True(
            File.Exists(servicePath),
            "Regieplanregeln müssen in einem Anwendungsservice liegen.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 310,
            "Der Regieplanservice muss als begrenzte Einheit unter 310 Zeilen bleiben.");
        Assert.Contains(
            "RunOfShowPlanService.EnsureInitialized(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "RunOfShowPlanService.Validate(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "RunOfShowPlanService.CreateAndActivatePlan(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "RunOfShowPlanService.ProjectRuntime(",
            runtime,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private async Task ExecuteRunOfShowStepAsync(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CloneRunOfShowStep",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TimedAutomationShell_DelegatesRuleSelectionAndValidation()
    {
        string root = FindRepositoryRoot();
        string shellPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Workflow",
            "MainWindow.Workflow.TimedAutomationRuntime.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "TimedAutomationRuleService.cs");
        string runtimeServicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "TimedAutomationRuntimeService.cs");
        string actionsPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Workflow",
            "MainWindow.Workflow.TimedAutomationActions.cs");
        string shell = File.ReadAllText(shellPath);
        string actions = File.ReadAllText(actionsPath);

        Assert.True(
            File.ReadLines(shellPath).Count() < 600,
            "Die Timed-Automation-Runtime muss unter 600 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(actionsPath).Count() < 475,
            "Der Timed-Automation-Aktions-Slice muss unter 475 Zeilen bleiben.");
        Assert.True(
            File.Exists(servicePath),
            "Auswahl und Validierung müssen in einem Anwendungsservice liegen.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 350,
            "Der Timed-Automation-Regelservice muss unter 350 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(runtimeServicePath).Count() < 100,
            "Die Runtime-Entscheidungen müssen unter 100 Zeilen bleiben.");
        Assert.Contains(
            "TimedAutomationRuleService.SelectDueRules(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "TimedAutomationRuleService.SelectWorkflowSteps(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "TimedAutomationRuleService.Validate(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "TimedAutomationRuntimeService.EvaluateDependency(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "TimedAutomationRuntimeService.ResolveExecutionPolicy(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "private async Task ExecuteTimedAutomationActionAsync(",
            actions,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private async Task ExecuteTimedAutomationActionAsync(",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ObsDashboardShell_DelegatesStreamAndProjectionRules()
    {
        string root = FindRepositoryRoot();
        string shellPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Obs",
            "MainWindow.Services.Obs.ConnectionDashboard.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "ObsDashboardApplicationService.cs");
        string streamObservationPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Obs",
            "MainWindow.Services.Obs.StreamObservation.cs");
        string shell = File.ReadAllText(shellPath);
        string streamObservation = File.ReadAllText(streamObservationPath);

        Assert.True(
            File.ReadLines(shellPath).Count() < 620,
            "Der OBS-Connection-/Dashboard-Slice muss unter 620 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(streamObservationPath).Count() < 330,
            "Der OBS-Stream-Observation-Slice muss unter 330 Zeilen bleiben.");
        Assert.True(
            File.Exists(servicePath),
            "OBS-Dashboard-Regeln müssen in einem Anwendungsservice liegen.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 200,
            "Der OBS-Dashboard-Service muss unter 200 Zeilen bleiben.");
        Assert.Contains(
            "ObsDashboardApplicationService.EvaluateStreamObservation(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "ObsDashboardApplicationService.SelectTrackedInput(",
            streamObservation,
            StringComparison.Ordinal);
        Assert.Contains(
            "ObsDashboardApplicationService.CreateSimpleVisibilityRule(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "ObsDashboardApplicationService.SelectSceneActivationRules(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "ObsDashboardApplicationService.ResolveLiveStartedAt(",
            streamObservation,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private async Task HandleObservedStreamStartAsync(",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TwitchViewerSample_RefreshesCommunityUiAfterLiveCountChanges()
    {
        string root = FindRepositoryRoot();
        string metricsPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.DashboardMetrics.cs");
        string metrics = File.ReadAllText(metricsPath);

        int viewerAssignment = metrics.IndexOf(
            "_currentLiveViewerCount =\n                Math.Max(0, status.ViewerCount);",
            StringComparison.Ordinal);
        int communityRefresh = metrics.IndexOf(
            "RefreshCommunityUi();",
            viewerAssignment,
            StringComparison.Ordinal);

        Assert.True(
            viewerAssignment >= 0,
            "Der Live-Zuschauerwert muss aus dem Twitch-Status übernommen werden.");
        Assert.True(
            communityRefresh > viewerAssignment,
            "Nach einem neuen Live-Zuschauerwert muss die Community-Anzeige direkt aktualisiert werden.");
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TwitchDashboardShell_DelegatesRaidProjectionRules()
    {
        string root = FindRepositoryRoot();
        string shellPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.DashboardRaid.cs");
        string chatPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.DashboardChat.cs");
        string metricsPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.DashboardMetrics.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "TwitchDashboardApplicationService.cs");
        string shell = File.ReadAllText(shellPath);

        Assert.True(
            File.ReadLines(shellPath).Count() < 450,
            "Der Twitch-Raid-Shell-Slice muss unter 450 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(chatPath).Count() < 220,
            "Der Twitch-Chat-Shell-Slice muss unter 220 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(metricsPath).Count() < 300,
            "Der Twitch-Metrik-Shell-Slice muss unter 300 Zeilen bleiben.");
        Assert.True(
            File.Exists(servicePath),
            "Twitch-Raid-Regeln müssen in einem Anwendungsservice liegen.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 250,
            "Der Twitch-Dashboard-Service muss unter 250 Zeilen bleiben.");
        Assert.Contains(
            "TwitchDashboardApplicationService.BuildRaidSuggestions(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "TwitchDashboardApplicationService.ProjectRaidActions(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "TwitchDashboardApplicationService.NormalizeRaidChannels(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "TwitchDashboardApplicationService.BuildRaidStatusProbeLogins(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private static bool MatchesRaidQuery(",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void TwitchProfessionalShell_DelegatesHistoryProjection()
    {
        string root = FindRepositoryRoot();
        string shellPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.ApiProfessional.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "TwitchProfessionalHistoryService.cs");
        string connectionPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.ConnectionChannel.cs");
        string moderationPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "Twitch",
            "MainWindow.Services.Twitch.ModerationChat.cs");
        string shell = File.ReadAllText(shellPath);

        Assert.True(
            File.ReadLines(shellPath).Count() < 420,
            "Der Twitch-Professional-Shell-Slice muss unter 420 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(connectionPath).Count() < 290,
            "Der Twitch-Connection-Shell-Slice muss unter 290 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(moderationPath).Count() < 250,
            "Der Twitch-Moderations-Shell-Slice muss unter 250 Zeilen bleiben.");
        Assert.True(
            File.Exists(servicePath),
            "Twitch-Historienprojektion muss in einem Anwendungsservice liegen.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 325,
            "Der Twitch-History-Service muss unter 325 Zeilen bleiben.");
        Assert.Contains(
            "TwitchProfessionalHistoryService.LoadAsync(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "JsonDocument.Parse(line)",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "static string PercentTrend(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private async Task AuthorizeTwitchAsync(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private async Task ModerateTwitchUserAsync(",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void StreamDeckCatalogShell_DelegatesMetadataAndProjectionRules()
    {
        string root = FindRepositoryRoot();
        string shellPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "StreamDeck",
            "MainWindow.Services.StreamDeck.Catalog.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "StreamDeckCatalogApplicationService.cs");
        string transferPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "StreamDeck",
            "MainWindow.Services.StreamDeck.CatalogTransfer.cs");
        string templatesPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "StreamDeck",
            "MainWindow.Services.StreamDeck.Templates.cs");
        string shell = File.ReadAllText(shellPath);

        Assert.True(
            File.ReadLines(shellPath).Count() < 600,
            "Der Stream-Deck-Katalog-Shell-Slice muss unter 600 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(transferPath).Count() < 220,
            "Der Stream-Deck-Transfer-Slice muss unter 220 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(templatesPath).Count() < 270,
            "Der Stream-Deck-Vorlagen-Slice muss unter 270 Zeilen bleiben.");
        Assert.True(
            File.Exists(servicePath),
            "Stream-Deck-Katalogregeln müssen in einem Anwendungsservice liegen.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 350,
            "Der Stream-Deck-Katalogservice muss unter 350 Zeilen bleiben.");
        Assert.Contains(
            "StreamDeckCatalogApplicationService.ProjectCatalog(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "StreamDeckCatalogApplicationService.FindFirstFreeSlot(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "StreamDeckCatalogApplicationService.CompareProfiles(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private static (string File, string Title",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private static (int DelayMs, int RetryCount",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void StreamDeckRuleShell_SeparatesActionAuthoring()
    {
        string root = FindRepositoryRoot();
        string rulesPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "StreamDeck",
            "MainWindow.Services.StreamDeck.Rules.cs");
        string actionsPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "StreamDeck",
            "MainWindow.Services.StreamDeck.Actions.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "StreamDeckAutomationRuleService.cs");
        string rules = File.ReadAllText(rulesPath);
        string actions = File.ReadAllText(actionsPath);

        Assert.True(
            File.ReadLines(rulesPath).Count() < 500,
            "Der Stream-Deck-Regel-Slice muss unter 500 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(actionsPath).Count() < 380,
            "Der Stream-Deck-Aktions-Slice muss unter 380 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 210,
            "Der Stream-Deck-Regelservice muss unter 210 Zeilen bleiben.");
        Assert.DoesNotContain(
            "private async Task CreateStreamDeckActionAsync(",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            "private async Task CreateStreamDeckActionAsync(",
            actions,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static string FormatStreamDeckCommandArgs(",
            actions,
            StringComparison.Ordinal);
        Assert.Contains(
            "StreamDeckAutomationRuleService.IsRuleMatch(",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            "StreamDeckAutomationRuleService.IsScheduleActive(",
            rules,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private bool IsStreamDeckRuleMatch(",
            rules,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void StreamerBotShell_DelegatesProtocolAndProjectionRules()
    {
        string root = FindRepositoryRoot();
        string shellPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "StreamerBot",
            "MainWindow.Services.StreamerBot.cs");
        string servicePath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Services",
            "StreamerBotApplicationService.cs");
        string connectionPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "Services",
            "StreamerBot",
            "MainWindow.Services.StreamerBot.ConnectionEvents.cs");
        string shell = File.ReadAllText(shellPath);
        string connection = File.ReadAllText(connectionPath);

        Assert.True(
            File.ReadLines(shellPath).Count() < 470,
            "Der Streamer.bot-Aktions-Slice muss unter 470 Zeilen bleiben.");
        Assert.True(
            File.ReadLines(connectionPath).Count() < 480,
            "Der Streamer.bot-Connection-Slice muss unter 480 Zeilen bleiben.");
        Assert.True(
            File.Exists(servicePath),
            "Streamer.bot-Protokollregeln müssen in einem Anwendungsservice liegen.");
        Assert.True(
            File.ReadLines(servicePath).Count() < 225,
            "Der Streamer.bot-Anwendungsservice muss unter 225 Zeilen bleiben.");
        Assert.Contains(
            "StreamerBotApplicationService.ParseActions(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "StreamerBotApplicationService.FilterActions(",
            shell,
            StringComparison.Ordinal);
        Assert.Contains(
            "StreamerBotApplicationService.TryParseEvent(",
            connection,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private async Task ConnectStreamerBotAsync(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private Dictionary<string, object?> ParseStreamerBotArguments(",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private static string BuildStreamerBotEventSummary(",
            shell,
            StringComparison.Ordinal);
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
            .Where(IsTrackedSourcePath)
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

    private static bool IsTrackedSourcePath(string path)
    {
        string[] segments = path.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return !segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase));
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
