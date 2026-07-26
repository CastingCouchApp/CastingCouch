namespace CreatorControlSuite.Modules.StreamDeck.Models;

public sealed record StreamDeckActionDefinition(
    string Id,
    string Title,
    string Command,
    string Description);

public sealed record StreamDeckProfilePackage(
    string Name,
    string Path,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StreamDeckActionDefinition> Actions);
