using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class RunOfShowPlanServiceTests
{
    [Fact]
    public void EnsureInitialized_MigratesLegacyStepsAndSelectsPlan()
    {
        var legacyStep = new RunOfShowStepSettings { Name = "Intro" };
        var workflow = new WorkflowSettings
        {
            RunOfShowSteps = [legacyStep],
            RunOfShowPlans = [],
            ActiveRunOfShowPlanId = ""
        };

        RunOfShowPlanSettings active =
            RunOfShowPlanService.EnsureInitialized(workflow);

        Assert.Single(workflow.RunOfShowPlans);
        Assert.Same(legacyStep, Assert.Single(active.Steps));
        Assert.Equal(active.Id, workflow.ActiveRunOfShowPlanId);
        Assert.Same(active.Steps, workflow.RunOfShowSteps);
    }

    [Fact]
    public void CloneStep_PreservesEveryConfigurableValue()
    {
        var source = new RunOfShowStepSettings
        {
            Id = "step-id",
            Name = "Intro",
            Enabled = false,
            ObsScene = "Starting",
            TransitionName = "Fade",
            TransitionDurationMilliseconds = 2345,
            SpotifyAction = "Playlist",
            SpotifyVolumePercent = 72,
            SpotifyPlaylistUri = "spotify:playlist:abc",
            SpotifyPlaylistShuffle = false,
            SpotifyActionDelaySeconds = 4,
            SpotifyFadeSeconds = 7,
            SpotifyPriority = 9,
            StreamerBotActionId = "action-id",
            StreamerBotActionName = "Action",
            ActionDelayMilliseconds = 456,
            ContinueOnActionError = true,
            UpdateTwitchChannel = true,
            TwitchTitle = "Titel",
            TwitchCategoryId = "category-id",
            TwitchCategoryName = "Kategorie",
            ContinueOnTwitchError = true,
            AutoAdvance = true,
            AutoAdvanceDelaySeconds = 17
        };

        RunOfShowStepSettings clone =
            RunOfShowPlanService.CloneStep(source);

        Assert.NotSame(source, clone);
        Assert.Equivalent(source, clone, strict: true);
    }

    [Fact]
    public void PrepareImportedStep_AssignsIdentityAndClampsRanges()
    {
        var source = new RunOfShowStepSettings
        {
            Id = "external-id",
            TransitionDurationMilliseconds = 50_000,
            SpotifyVolumePercent = -4,
            ActionDelayMilliseconds = 90_000,
            AutoAdvanceDelaySeconds = 0
        };

        RunOfShowStepSettings imported =
            RunOfShowPlanService.PrepareImportedStep(source);

        Assert.NotEqual(source.Id, imported.Id);
        Assert.Equal(20_000, imported.TransitionDurationMilliseconds);
        Assert.Equal(0, imported.SpotifyVolumePercent);
        Assert.Equal(60_000, imported.ActionDelayMilliseconds);
        Assert.Equal(1, imported.AutoAdvanceDelaySeconds);
    }

    [Fact]
    public void Validate_ReportsPlanIssuesWithoutUiDependencies()
    {
        RunOfShowStepSettings[] steps =
        [
            new()
            {
                Name = "Intro",
                ObsScene = "Missing",
                UpdateTwitchChannel = true,
                AutoAdvance = true,
                AutoAdvanceDelaySeconds = 0
            },
            new()
            {
                Name = "intro",
                ObsScene = "Known"
            },
            new()
            {
                Name = "",
                Enabled = false
            }
        ];

        IReadOnlyList<string> issues = RunOfShowPlanService.Validate(
            steps,
            ["Known"],
            obsConnected: true);

        Assert.Contains("Doppelter Schrittname: Intro", issues);
        Assert.Contains(
            issues,
            issue => issue.Contains(
                "OBS-Szene 'Missing' wurde nicht gefunden.",
                StringComparison.Ordinal));
        Assert.Contains(
            issues,
            issue => issue.Contains(
                "Titel und Kategorie fehlen.",
                StringComparison.Ordinal));
        Assert.Contains(
            issues,
            issue => issue.Contains(
                "mindestens 1 Sekunde",
                StringComparison.Ordinal));
        Assert.Contains(
            issues,
            issue => issue.Contains("Name fehlt.", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateAndActivatePlan_GeneratesUniqueNameAndSynchronizesLegacySteps()
    {
        var workflow = new WorkflowSettings
        {
            RunOfShowPlans =
            [
                new() { Name = "Neuer Regieplan" },
                new() { Name = "neuer regieplan 2" }
            ]
        };

        RunOfShowPlanSettings created =
            RunOfShowPlanService.CreateAndActivatePlan(workflow);

        Assert.Equal("Neuer Regieplan 3", created.Name);
        Assert.Same(created, workflow.RunOfShowPlans[^1]);
        Assert.Equal(created.Id, workflow.ActiveRunOfShowPlanId);
        Assert.Same(created.Steps, workflow.RunOfShowSteps);
    }

    [Fact]
    public void RenamePlan_RejectsBlankAndDuplicateNames()
    {
        var first = new RunOfShowPlanSettings { Name = "Show" };
        var second = new RunOfShowPlanSettings { Name = "Podcast" };
        RunOfShowPlanSettings[] plans = [first, second];

        string? blankError =
            RunOfShowPlanService.RenamePlan(first, plans, " ");
        string? duplicateError =
            RunOfShowPlanService.RenamePlan(first, plans, " podcast ");
        string? success =
            RunOfShowPlanService.RenamePlan(first, plans, " Abendshow ");

        Assert.Equal("Bitte einen Namen für den Regieplan eingeben.", blankError);
        Assert.Equal(
            "Ein Regieplan mit diesem Namen existiert bereits.",
            duplicateError);
        Assert.Null(success);
        Assert.Equal("Abendshow", first.Name);
    }

    [Fact]
    public void DeletePlanAndActivateNext_SynchronizesWorkflow()
    {
        var first = new RunOfShowPlanSettings
        {
            Name = "First",
            Steps = [new() { Name = "Intro" }]
        };
        var second = new RunOfShowPlanSettings
        {
            Name = "Second",
            Steps = [new() { Name = "Main" }]
        };
        var workflow = new WorkflowSettings
        {
            RunOfShowPlans = [first, second],
            ActiveRunOfShowPlanId = second.Id,
            RunOfShowSteps = second.Steps
        };

        RunOfShowPlanSettings active =
            RunOfShowPlanService.DeletePlanAndActivateNext(workflow, second);

        Assert.Same(first, active);
        Assert.Single(workflow.RunOfShowPlans);
        Assert.Equal(first.Id, workflow.ActiveRunOfShowPlanId);
        Assert.Same(first.Steps, workflow.RunOfShowSteps);
    }

    [Fact]
    public void ProjectRuntime_SelectsNextEnabledStepAndFormatsProgress()
    {
        RunOfShowStepSettings[] steps =
        [
            new() { Name = "Disabled", Enabled = false },
            new() { Name = "Intro", Enabled = true },
            new() { Name = "Main", Enabled = true }
        ];

        RunOfShowRuntimeProjection initial =
            RunOfShowPlanService.ProjectRuntime(steps, -1);
        RunOfShowRuntimeProjection running =
            RunOfShowPlanService.ProjectRuntime(steps, 1);

        Assert.Equal(1, initial.NextEnabledIndex);
        Assert.Equal("Noch nicht gestartet", initial.CurrentName);
        Assert.Equal("Intro", initial.NextName);
        Assert.Equal("0 / 3", initial.Progress);
        Assert.Equal(2, running.NextEnabledIndex);
        Assert.Equal("Intro", running.CurrentName);
        Assert.Equal("Main", running.NextName);
        Assert.Equal("2 / 3", running.Progress);
    }
}
