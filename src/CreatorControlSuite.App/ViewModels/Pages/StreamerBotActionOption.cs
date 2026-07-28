namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed record StreamerBotActionOption(
    string Id,
    string Name,
    string Group,
    bool Enabled)
{
    public string DisplayName =>
        $"{(Enabled ? "" : "[DEAKTIVIERT] ")}{Group} · {Name}";
}
