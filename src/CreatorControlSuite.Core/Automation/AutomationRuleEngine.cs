namespace CreatorControlSuite.Core.Automation;

/// <summary>
/// Shared trigger/action rule model for StreamDeck, timed workflow, and Spotify automation.
/// </summary>
public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public bool FireOnce { get; set; }
    public TimeSpan? HoldDuration { get; set; }
    public string TriggerType { get; set; } = "";
    public string ActionType { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record AutomationContext(
    string TriggerType,
    IReadOnlyDictionary<string, string> Variables,
    DateTimeOffset OccurredAt);

public interface IAutomationActionHandler
{
    string ActionType { get; }

    Task ExecuteAsync(
        AutomationRule rule,
        AutomationContext context,
        CancellationToken cancellationToken = default);
}

public interface IAutomationRuleEngine
{
    IReadOnlyList<AutomationRule> Rules { get; }

    void ReplaceRules(IEnumerable<AutomationRule> rules);

    Task HandleTriggerAsync(
        AutomationContext context,
        CancellationToken cancellationToken = default);
}

public sealed class AutomationRuleEngine(IEnumerable<IAutomationActionHandler> handlers) : IAutomationRuleEngine
{
    private readonly IReadOnlyDictionary<string, IAutomationActionHandler> _handlers = handlers.ToDictionary(
            handler => handler.ActionType,
            StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();
    private List<AutomationRule> _rules = [];
    private readonly HashSet<string> _firedOnce = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastFired = new(StringComparer.Ordinal);

    public IReadOnlyList<AutomationRule> Rules
    {
        get
        {
            lock (_sync)
            {
                return [.. _rules];
            }
        }
    }

    public void ReplaceRules(IEnumerable<AutomationRule> rules)
    {
        lock (_sync)
        {
            _rules = [.. rules.OrderBy(rule => rule.Priority)];
        }
    }

    public async Task HandleTriggerAsync(
        AutomationContext context,
        CancellationToken cancellationToken = default)
    {
        List<AutomationRule> candidates;
        lock (_sync)
        {
            candidates = [.. _rules
                .Where(rule =>
                    rule.Enabled &&
                    string.Equals(
                        rule.TriggerType,
                        context.TriggerType,
                        StringComparison.OrdinalIgnoreCase))];
        }

        foreach (AutomationRule rule in candidates)
        {
            if (rule.FireOnce && _firedOnce.Contains(rule.Id))
            {
                continue;
            }

            if (rule.HoldDuration is { } hold &&
                _lastFired.TryGetValue(rule.Id, out DateTimeOffset last) &&
                DateTimeOffset.UtcNow - last < hold)
            {
                continue;
            }

            if (!_handlers.TryGetValue(rule.ActionType, out IAutomationActionHandler? handler))
            {
                continue;
            }

            await handler.ExecuteAsync(rule, context, cancellationToken);

            _lastFired[rule.Id] = DateTimeOffset.UtcNow;
            if (rule.FireOnce)
            {
                _firedOnce.Add(rule.Id);
            }
        }
    }
}
