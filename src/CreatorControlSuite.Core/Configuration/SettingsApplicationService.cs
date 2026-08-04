using CreatorControlSuite.Core.Validation;

namespace CreatorControlSuite.Core.Configuration;

public sealed class SettingsApplicationService(
    ISettingsStore store,
    ISettingsValidator validator)
{
    private static readonly string[] RequiredTwitchScopes =
    [
        "channel:read:guest_star",
        "moderator:manage:banned_users"
    ];

    public async Task<AppSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await store.LoadAsync(cancellationToken);
        if (Normalize(settings))
        {
            await store.SaveAsync(settings, cancellationToken);
        }

        return settings;
    }

    public ValidationReport Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return validator.Validate(settings);
    }

    public async Task<ValidationReport> SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidationReport report = validator.Validate(settings);
        if (report.IsValid)
        {
            await store.SaveAsync(settings, cancellationToken);
        }

        return report;
    }

    private static bool Normalize(AppSettings settings)
    {
        bool changed = false;
        changed |= EnsureList(
            settings.Workflow.TimedAutomations,
            value => settings.Workflow.TimedAutomations = value);
        changed |= EnsureList(
            settings.Workflow.RunOfShowSteps,
            value => settings.Workflow.RunOfShowSteps = value);
        changed |= EnsureList(
            settings.Workflow.RunOfShowPlans,
            value => settings.Workflow.RunOfShowPlans = value);
        changed |= EnsureList(
            settings.Dashboard.SceneButtons,
            value => settings.Dashboard.SceneButtons = value);
        changed |= EnsureList(
            settings.Obs.AudioProfiles,
            value => settings.Obs.AudioProfiles = value);

        if (settings.Alerts.AutoCreateObsSources)
        {
            settings.Alerts.AutoCreateObsSources = false;
            changed = true;
        }

        settings.Twitch.Scopes ??= [];
        string[] missingTwitchScopes = RequiredTwitchScopes
            .Except(settings.Twitch.Scopes, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingTwitchScopes.Length > 0)
        {
            settings.Twitch.Scopes =
            [
                .. settings.Twitch.Scopes,
                .. missingTwitchScopes
            ];
            changed = true;
        }

        settings.Overlay.EnsureInstancesMigrated();
        int previousCanvasCount = settings.Overlay.Canvases?.Count ?? 0;
        settings.Overlay.EnsureCanvasesMigrated();
        return changed ||
               previousCanvasCount != settings.Overlay.Canvases!.Count;
    }

    private static bool EnsureList<T>(
        List<T>? current,
        Action<List<T>> assign)
    {
        if (current is not null)
        {
            return false;
        }

        assign([]);
        return true;
    }
}
