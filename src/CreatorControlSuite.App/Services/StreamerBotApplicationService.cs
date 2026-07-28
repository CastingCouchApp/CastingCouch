using System.Text.Json;
using CreatorControlSuite.App.ViewModels.Pages;

namespace CreatorControlSuite.App.Services;

public sealed record StreamerBotEventProjection(
    string Source,
    string Type,
    string Summary,
    bool IsKnownAlert);

public static class StreamerBotApplicationService
{
    public static IReadOnlyList<StreamerBotActionOption> ParseActions(
        JsonElement root)
    {
        if (!root.TryGetProperty("actions", out JsonElement actionsElement) ||
            actionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Streamer.bot hat keine Aktionsliste zurückgegeben.");
        }

        var actions = new List<StreamerBotActionOption>();
        foreach (JsonElement action in actionsElement.EnumerateArray())
        {
            string id = ReadString(action, "id") ?? "";
            string name = ReadString(action, "name") ?? "";
            string group = ReadString(action, "group") ?? "Ohne Gruppe";
            bool enabled =
                !action.TryGetProperty(
                    "enabled",
                    out JsonElement enabledNode) ||
                enabledNode.ValueKind != JsonValueKind.False;
            if (!string.IsNullOrWhiteSpace(name))
            {
                actions.Add(new(id, name, group, enabled));
            }
        }

        return
        [
            .. actions
                .OrderBy(action => action.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static IReadOnlyList<StreamerBotActionOption> FilterActions(
        IEnumerable<StreamerBotActionOption> source,
        IEnumerable<string> favoriteActionIds,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(favoriteActionIds);
        var favorites = new HashSet<string>(
            favoriteActionIds,
            StringComparer.OrdinalIgnoreCase);
        string query = search?.Trim() ?? "";
        return
        [
            .. source
                .Where(action =>
                    string.IsNullOrWhiteSpace(query) ||
                    action.Name.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase) ||
                    action.Group.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(action => favorites.Contains(action.Id))
                .ThenBy(
                    action => action.Group,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    action => action.Name,
                    StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static IReadOnlyList<string> SelectGroups(
        IEnumerable<StreamerBotActionOption> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return
        [
            .. source
                .Select(action => action.Group)
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static Dictionary<string, object?> ParseArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Die Parameter müssen ein JSON-Objekt sein.");
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            document.RootElement.GetRawText()) ?? [];
    }

    public static string FormatArguments(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Die Parameter müssen ein JSON-Objekt sein.");
        }

        return JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions { WriteIndented = true });
    }

    public static StreamerBotEventProjection ParseEvent(JsonElement root) =>
        TryParseEvent(root) ??
        throw new InvalidOperationException(
            "Streamer.bot-Nachricht enthält kein Ereignis.");

    public static StreamerBotEventProjection? TryParseEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out JsonElement eventNode) ||
            eventNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string source = ReadString(eventNode, "source") ?? "Streamer.bot";
        string type = ReadString(eventNode, "type") ?? "Alert";
        JsonElement data =
            root.TryGetProperty("data", out JsonElement dataNode) &&
            dataNode.ValueKind == JsonValueKind.Object
                ? dataNode
                : root;
        string? user = ReadString(
            data,
            "user_name",
            "userName",
            "displayName",
            "user",
            "from");
        string? message = ReadString(
            data,
            "message",
            "text",
            "input",
            "reason");
        string? amount = ReadString(
            data,
            "amount",
            "bits",
            "months",
            "viewers");
        string summary = BuildSummary(source, type, user, amount, message);
        string normalized = $"{source} {type}";
        bool isKnownAlert =
            normalized.Contains("follow", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("cheer", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("sub", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("raid", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("alert", StringComparison.OrdinalIgnoreCase);
        return new(source, type, summary, isKnownAlert);
    }

    private static string BuildSummary(
        string source,
        string type,
        params string?[] values)
    {
        string[] parts =
        [
            .. values.Where(value => !string.IsNullOrWhiteSpace(value))!
        ];
        return parts.Length > 0
            ? string.Join(" · ", parts)
            : $"{source} · {type}";
    }

    private static string? ReadString(
        JsonElement element,
        params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}
