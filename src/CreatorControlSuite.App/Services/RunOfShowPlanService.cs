using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Services;

public sealed record RunOfShowRuntimeProjection(
    int NextEnabledIndex,
    string CurrentName,
    string NextName,
    string Progress);

public static class RunOfShowPlanService
{
    public static RunOfShowPlanSettings EnsureInitialized(
        WorkflowSettings workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        workflow.RunOfShowPlans ??= [];
        workflow.RunOfShowSteps ??= [];

        if (workflow.RunOfShowPlans.Count == 0)
        {
            var initialPlan = new RunOfShowPlanSettings
            {
                Name = "Standard",
                Steps = workflow.RunOfShowSteps
            };
            workflow.RunOfShowPlans.Add(initialPlan);
            workflow.ActiveRunOfShowPlanId = initialPlan.Id;
        }

        RunOfShowPlanSettings active =
            workflow.RunOfShowPlans.FirstOrDefault(plan =>
                string.Equals(
                    plan.Id,
                    workflow.ActiveRunOfShowPlanId,
                    StringComparison.OrdinalIgnoreCase))
            ?? workflow.RunOfShowPlans[0];
        active.Steps ??= [];
        workflow.ActiveRunOfShowPlanId = active.Id;
        workflow.RunOfShowSteps = active.Steps;
        return active;
    }

    public static RunOfShowStepSettings CloneStep(
        RunOfShowStepSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new RunOfShowStepSettings
        {
            Id = source.Id,
            Name = source.Name,
            Enabled = source.Enabled,
            ObsScene = source.ObsScene,
            TransitionName = source.TransitionName,
            TransitionDurationMilliseconds =
                source.TransitionDurationMilliseconds,
            SpotifyAction = source.SpotifyAction,
            SpotifyVolumePercent = source.SpotifyVolumePercent,
            SpotifyPlaylistUri = source.SpotifyPlaylistUri,
            SpotifyPlaylistShuffle = source.SpotifyPlaylistShuffle,
            SpotifyActionDelaySeconds = source.SpotifyActionDelaySeconds,
            SpotifyFadeSeconds = source.SpotifyFadeSeconds,
            SpotifyPriority = source.SpotifyPriority,
            StreamerBotActionId = source.StreamerBotActionId,
            StreamerBotActionName = source.StreamerBotActionName,
            ActionDelayMilliseconds = source.ActionDelayMilliseconds,
            ContinueOnActionError = source.ContinueOnActionError,
            UpdateTwitchChannel = source.UpdateTwitchChannel,
            TwitchTitle = source.TwitchTitle,
            TwitchCategoryId = source.TwitchCategoryId,
            TwitchCategoryName = source.TwitchCategoryName,
            ContinueOnTwitchError = source.ContinueOnTwitchError,
            AutoAdvance = source.AutoAdvance,
            AutoAdvanceDelaySeconds = source.AutoAdvanceDelaySeconds
        };
    }

    public static RunOfShowStepSettings PrepareImportedStep(
        RunOfShowStepSettings source)
    {
        RunOfShowStepSettings imported = CloneStep(source);
        imported.Id = Guid.NewGuid().ToString("N");
        imported.TransitionDurationMilliseconds = Math.Clamp(
            imported.TransitionDurationMilliseconds,
            0,
            20_000);
        imported.SpotifyVolumePercent = Math.Clamp(
            imported.SpotifyVolumePercent,
            0,
            100);
        imported.ActionDelayMilliseconds = Math.Clamp(
            imported.ActionDelayMilliseconds,
            0,
            60_000);
        imported.AutoAdvanceDelaySeconds = Math.Clamp(
            imported.AutoAdvanceDelaySeconds,
            1,
            86_400);
        return imported;
    }

    public static RunOfShowPlanSettings CreateAndActivatePlan(
        WorkflowSettings workflow,
        string baseName = "Neuer Regieplan")
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        EnsureInitialized(workflow);
        string name = baseName;
        int counter = 2;
        while (workflow.RunOfShowPlans.Any(plan =>
                   string.Equals(
                       plan.Name,
                       name,
                       StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {counter++}";
        }

        var plan = new RunOfShowPlanSettings { Name = name };
        workflow.RunOfShowPlans.Add(plan);
        ActivatePlan(workflow, plan);
        return plan;
    }

    public static void ActivatePlan(
        WorkflowSettings workflow,
        RunOfShowPlanSettings plan)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(plan);
        if (!workflow.RunOfShowPlans.Contains(plan))
        {
            throw new ArgumentException(
                "Der Regieplan gehört nicht zu diesem Workflow.",
                nameof(plan));
        }

        plan.Steps ??= [];
        workflow.ActiveRunOfShowPlanId = plan.Id;
        workflow.RunOfShowSteps = plan.Steps;
    }

    public static string? RenamePlan(
        RunOfShowPlanSettings plan,
        IEnumerable<RunOfShowPlanSettings> plans,
        string? requestedName)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plans);
        string name = requestedName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Bitte einen Namen für den Regieplan eingeben.";
        }

        if (plans.Any(candidate =>
                candidate != plan &&
                string.Equals(
                    candidate.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "Ein Regieplan mit diesem Namen existiert bereits.";
        }

        plan.Name = name;
        return null;
    }

    public static RunOfShowPlanSettings DeletePlanAndActivateNext(
        WorkflowSettings workflow,
        RunOfShowPlanSettings plan)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(plan);
        if (workflow.RunOfShowPlans.Count <= 1)
        {
            throw new InvalidOperationException(
                "Der letzte Regieplan kann nicht gelöscht werden.");
        }

        if (!workflow.RunOfShowPlans.Remove(plan))
        {
            throw new ArgumentException(
                "Der Regieplan gehört nicht zu diesem Workflow.",
                nameof(plan));
        }

        RunOfShowPlanSettings next = workflow.RunOfShowPlans[0];
        ActivatePlan(workflow, next);
        return next;
    }

    public static RunOfShowRuntimeProjection ProjectRuntime(
        IReadOnlyList<RunOfShowStepSettings> steps,
        int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(steps);
        int nextIndex = -1;
        for (int index = Math.Max(0, currentIndex + 1);
             index < steps.Count;
             index++)
        {
            if (steps[index].Enabled)
            {
                nextIndex = index;
                break;
            }
        }

        string currentName =
            currentIndex >= 0 && currentIndex < steps.Count
                ? steps[currentIndex].Name
                : "Noch nicht gestartet";
        string nextName =
            nextIndex >= 0
                ? steps[nextIndex].Name
                : "Kein weiterer Schritt";
        string progress =
            steps.Count == 0
                ? "0 / 0"
                : $"{Math.Max(0, currentIndex + 1)} / {steps.Count}";
        return new(nextIndex, currentName, nextName, progress);
    }

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<RunOfShowStepSettings> steps,
        IEnumerable<string> obsSceneNames,
        bool obsConnected)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(obsSceneNames);

        var issues = new List<string>();
        var sceneNames = obsSceneNames.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> duplicateNames = steps
            .Where(step => step.Enabled)
            .GroupBy(
                step => step.Name.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Where(group =>
                !string.IsNullOrWhiteSpace(group.Key) &&
                group.Count() > 1)
            .Select(group => group.Key);
        foreach (string name in duplicateNames)
        {
            issues.Add($"Doppelter Schrittname: {name}");
        }

        for (int index = 0; index < steps.Count; index++)
        {
            RunOfShowStepSettings step = steps[index];
            string displayName = string.IsNullOrWhiteSpace(step.Name)
                ? "ohne Name"
                : step.Name;
            string label = $"Schritt {index + 1} ({displayName})";
            if (string.IsNullOrWhiteSpace(step.Name))
            {
                issues.Add(label + ": Name fehlt.");
            }

            if (step.Enabled && string.IsNullOrWhiteSpace(step.ObsScene))
            {
                issues.Add(label + ": Keine OBS-Szene ausgewählt.");
            }
            else if (step.Enabled &&
                     obsConnected &&
                     !sceneNames.Contains(step.ObsScene))
            {
                issues.Add(
                    label +
                    $": OBS-Szene '{step.ObsScene}' wurde nicht gefunden.");
            }

            if (step.UpdateTwitchChannel &&
                string.IsNullOrWhiteSpace(step.TwitchTitle) &&
                string.IsNullOrWhiteSpace(step.TwitchCategoryId))
            {
                issues.Add(
                    label +
                    ": Twitch-Aktualisierung ist aktiv, " +
                    "aber Titel und Kategorie fehlen.");
            }

            if (step.AutoAdvance && step.AutoAdvanceDelaySeconds < 1)
            {
                issues.Add(
                    label +
                    ": Automatische Wartezeit muss mindestens " +
                    "1 Sekunde betragen.");
            }
        }

        return issues;
    }
}
