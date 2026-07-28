using System.Globalization;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.OBS.Models;

namespace CreatorControlSuite.App.Services;

public sealed record ObsStreamObservation(
    bool IsActive,
    int ConsecutiveInactivePolls,
    bool Started,
    bool Ended);

public static class ObsDashboardApplicationService
{
    public static IReadOnlyList<TimedAutomationRuleSettings>
        SelectSimpleVisibilityRules(
            IEnumerable<TimedAutomationRuleSettings> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return
        [
            .. rules
                .Where(rule =>
                    rule.TriggerType is "SceneElapsed" or "StreamElapsed" &&
                    string.Equals(
                        rule.ActionType,
                        "SetSourceVisibility",
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(rule => rule.DelaySeconds)
        ];
    }

    public static TimedAutomationRuleSettings CreateSimpleVisibilityRule(
        string scene,
        string source,
        int delaySeconds,
        bool visible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfNegative(delaySeconds);

        return new TimedAutomationRuleSettings
        {
            Name =
                $"{scene} → {source}: nach {delaySeconds} Sek. " +
                (visible ? "einblenden" : "ausblenden"),
            Enabled = true,
            TriggerType = "SceneElapsed",
            TriggerScene = scene,
            DelaySeconds = delaySeconds,
            ActionType = "SetSourceVisibility",
            ObsScene = scene,
            ObsSource = source,
            SourceVisible = visible,
            OncePerStream = true
        };
    }

    public static IReadOnlyList<TimedAutomationRuleSettings>
        SelectSceneActivationRules(
            IEnumerable<TimedAutomationRuleSettings> rules,
            string scene)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        return
        [
            .. rules.Where(rule =>
                string.Equals(
                    rule.TriggerType,
                    "SceneElapsed",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    rule.TriggerScene,
                    scene,
                    StringComparison.OrdinalIgnoreCase))
        ];
    }

    public static IReadOnlyList<string> SelectDistinctSceneNames(
        IEnumerable<ObsSceneInfo> scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        return
        [
            .. scenes
                .Select(scene => scene.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static DateTimeOffset? ResolveLiveStartedAt(
        DateTimeOffset? twitchStartedAt,
        DateTimeOffset? obsSessionStartedAt,
        DateTimeOffset? twitchObservedAt) =>
        twitchStartedAt ?? obsSessionStartedAt ?? twitchObservedAt;

    public static ObsStreamObservation EvaluateStreamObservation(
        bool reportsActive,
        bool snapshotConnected,
        bool wasActive,
        bool hasSessionStart,
        int consecutiveInactivePolls,
        int requiredInactivePolls)
    {
        int threshold = Math.Max(1, requiredInactivePolls);
        int inactivePolls = Math.Max(0, consecutiveInactivePolls);
        bool hasActiveLatch = wasActive || hasSessionStart;

        if (reportsActive)
        {
            inactivePolls = 0;
        }
        else if (snapshotConnected && hasActiveLatch)
        {
            inactivePolls++;
        }

        bool isActive = reportsActive ||
            (hasActiveLatch && inactivePolls < threshold);
        return new ObsStreamObservation(
            isActive,
            inactivePolls,
            Started: isActive && !wasActive,
            Ended: !isActive && wasActive);
    }

    public static DateTimeOffset ResolveObservedStreamStartedAt(
        string? outputTimecode,
        DateTimeOffset observedAt)
    {
        if (!string.IsNullOrWhiteSpace(outputTimecode) &&
            TimeSpan.TryParse(
                outputTimecode,
                CultureInfo.InvariantCulture,
                out TimeSpan elapsed) &&
            elapsed >= TimeSpan.Zero &&
            elapsed < TimeSpan.FromDays(30))
        {
            return observedAt - elapsed;
        }

        return observedAt;
    }

    public static ObsInputInfo? SelectTrackedInput(
        IReadOnlyList<ObsInputInfo> inputs,
        string? configuredSource,
        IReadOnlyList<string> preferredExactNames,
        IReadOnlyList<string> fallbackNameParts)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(preferredExactNames);
        ArgumentNullException.ThrowIfNull(fallbackNameParts);

        ObsInputInfo? input = null;
        if (!string.IsNullOrWhiteSpace(configuredSource))
        {
            input = inputs.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    configuredSource.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        input ??= preferredExactNames
            .Select(name => inputs.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(candidate => candidate is not null);

        return input ?? inputs.FirstOrDefault(candidate =>
            fallbackNameParts.Any(part =>
                candidate.Name.Contains(
                    part,
                    StringComparison.OrdinalIgnoreCase)));
    }
}
