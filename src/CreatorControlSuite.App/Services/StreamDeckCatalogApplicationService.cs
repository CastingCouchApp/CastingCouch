using System.Text.Json;

namespace CreatorControlSuite.App.Services;

public sealed record StreamDeckCatalogEntry(
    string File,
    string Title,
    string Command,
    string Parameter,
    string Profile,
    string Page,
    int Slot,
    int Steps,
    bool Locked,
    string Condition,
    string TrueLabel,
    string FalseLabel);

public sealed record StreamDeckToggleMetadata(
    bool ToggleMode,
    string AlternateCommand,
    string AlternateParameter);

public sealed record StreamDeckExecutionPolicy(
    int DelayMs,
    int RetryCount,
    int CooldownMs);

public sealed record StreamDeckCatalogProjection(
    IReadOnlyList<StreamDeckCatalogEntry> Entries,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Pages,
    int OccupiedPositions,
    int Conflicts);

public static class StreamDeckCatalogApplicationService
{
    private const string DefaultProfile = "Standard";
    private const string DefaultPage = "Hauptseite";

    public static StreamDeckCatalogEntry ReadMetadata(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        try
        {
            string metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath))
            {
                return CreateFallback(file);
            }

            using JsonDocument document =
                JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement root = document.RootElement;
            string ReadString(string name, string fallback) =>
                root.TryGetProperty(name, out JsonElement node)
                    ? node.GetString() ?? fallback
                    : fallback;
            int slot =
                root.TryGetProperty("slot", out JsonElement slotNode) &&
                slotNode.TryGetInt32(out int slotValue)
                    ? slotValue
                    : 0;
            int steps =
                root.TryGetProperty("steps", out JsonElement stepsNode) &&
                stepsNode.ValueKind == JsonValueKind.Array
                    ? stepsNode.GetArrayLength()
                    : 1;
            bool locked =
                root.TryGetProperty("locked", out JsonElement lockedNode) &&
                lockedNode.ValueKind == JsonValueKind.True;
            return new StreamDeckCatalogEntry(
                file,
                ReadString("title", Path.GetFileNameWithoutExtension(file)),
                ReadString("command", "–"),
                ReadString("parameter", ""),
                ReadString("profile", DefaultProfile),
                ReadString("page", DefaultPage),
                slot,
                Math.Max(1, steps),
                locked,
                ReadString("condition", ""),
                ReadString("trueLabel", ""),
                ReadString("falseLabel", ""));
        }
        catch (JsonException)
        {
            return CreateFallback(file);
        }
        catch (IOException)
        {
            return CreateFallback(file);
        }
    }

    public static StreamDeckToggleMetadata ReadToggleMetadata(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        try
        {
            string metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath))
            {
                return new(false, "", "");
            }

            using JsonDocument document =
                JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement root = document.RootElement;
            bool toggle =
                root.TryGetProperty(
                    "toggleMode",
                    out JsonElement toggleNode) &&
                toggleNode.ValueKind == JsonValueKind.True;
            string command =
                root.TryGetProperty(
                    "alternateCommand",
                    out JsonElement commandNode)
                    ? commandNode.GetString() ?? ""
                    : "";
            string parameter =
                root.TryGetProperty(
                    "alternateParameter",
                    out JsonElement parameterNode)
                    ? parameterNode.GetString() ?? ""
                    : "";
            return new(toggle, command, parameter);
        }
        catch (JsonException)
        {
            return new(false, "", "");
        }
        catch (IOException)
        {
            return new(false, "", "");
        }
    }

    public static StreamDeckExecutionPolicy ReadExecutionPolicy(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        try
        {
            string metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath))
            {
                return DefaultExecutionPolicy();
            }

            using JsonDocument document =
                JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement root = document.RootElement;
            int ReadInt(string name, int fallback) =>
                root.TryGetProperty(name, out JsonElement node) &&
                node.TryGetInt32(out int value)
                    ? value
                    : fallback;
            return new StreamDeckExecutionPolicy(
                Math.Clamp(ReadInt("stepDelayMs", 250), 0, 10000),
                Math.Clamp(ReadInt("retryCount", 1), 0, 5),
                Math.Clamp(ReadInt("cooldownMs", 1000), 0, 60000));
        }
        catch (JsonException)
        {
            return DefaultExecutionPolicy();
        }
        catch (IOException)
        {
            return DefaultExecutionPolicy();
        }
    }

    public static StreamDeckCatalogProjection ProjectCatalog(
        IEnumerable<StreamDeckCatalogEntry> source,
        string? selectedProfile,
        string? selectedPage)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<StreamDeckCatalogEntry> sourceEntries = [.. source];
        List<StreamDeckCatalogEntry> entries =
        [
            .. sourceEntries
                .OrderBy(entry => entry.Profile, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Page, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Slot)
                .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
        ];
        IReadOnlyList<string> profiles = DistinctValues(
            sourceEntries.Select(entry => entry.Profile));
        IReadOnlyList<string> pages = DistinctValues(
            sourceEntries
                .Where(entry => Matches(entry.Profile, selectedProfile))
                .Select(entry => entry.Page));
        IReadOnlyList<StreamDeckCatalogEntry> filtered =
        [
            .. entries.Where(entry =>
                Matches(entry.Profile, selectedProfile) &&
                Matches(entry.Page, selectedPage))
        ];
        int occupied = entries
            .Select(PositionKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        int conflicts = entries
            .GroupBy(PositionKey, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);
        return new(filtered, profiles, pages, occupied, conflicts);
    }

    public static string ResolveDisplayTitle(
        StreamDeckCatalogEntry entry,
        IReadOnlyDictionary<string, bool> states)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(states);
        if (string.IsNullOrWhiteSpace(entry.Condition) ||
            !states.TryGetValue(entry.Condition, out bool active))
        {
            return entry.Title;
        }

        string label = active ? entry.TrueLabel : entry.FalseLabel;
        return string.IsNullOrWhiteSpace(label) ? entry.Title : label;
    }

    public static int FindFirstFreeSlot(
        IEnumerable<StreamDeckCatalogEntry> entries,
        string profile,
        string page,
        string? excludedFile)
    {
        ArgumentNullException.ThrowIfNull(entries);
        HashSet<int> used =
        [
            .. entries
                .Where(entry =>
                    !string.Equals(
                        entry.File,
                        excludedFile,
                        StringComparison.OrdinalIgnoreCase) &&
                    Matches(entry.Profile, profile) &&
                    Matches(entry.Page, page))
                .Select(entry => entry.Slot)
        ];
        return Enumerable.Range(1, 32)
            .FirstOrDefault(slot => !used.Contains(slot));
    }

    public static IReadOnlyList<string> CompareProfiles(
        IEnumerable<StreamDeckCatalogEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<StreamDeckCatalogEntry> entries = [.. source];
        List<string> profiles =
        [
            .. DistinctValues(entries.Select(entry => entry.Profile))
        ];
        if (profiles.Count < 2)
        {
            return [];
        }

        string baseline = profiles[0];
        HashSet<string> baseKeys = KeysForProfile(entries, baseline);
        var lines = new List<string> { $"Vergleichsbasis: {baseline}" };
        foreach (string profile in profiles.Skip(1))
        {
            HashSet<string> keys = KeysForProfile(entries, profile);
            lines.Add(
                $"{profile}: {keys.Count} Tasten · " +
                $"+{keys.Except(baseKeys).Count()} hinzugefügt · " +
                $"-{baseKeys.Except(keys).Count()} fehlend");
        }

        return lines;
    }

    private static IReadOnlyList<string> DistinctValues(
        IEnumerable<string> values) =>
    [
        .. values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
    ];

    private static bool Matches(string value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);

    private static string PositionKey(StreamDeckCatalogEntry entry) =>
        $"{entry.Profile}|{entry.Page}|{entry.Slot}";

    private static HashSet<string> KeysForProfile(
        IEnumerable<StreamDeckCatalogEntry> entries,
        string profile) =>
        entries
            .Where(entry => Matches(entry.Profile, profile))
            .Select(entry =>
                $"{entry.Page}|{entry.Slot}|{entry.Command}|{entry.Parameter}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static StreamDeckCatalogEntry CreateFallback(string file) =>
        new(
            file,
            Path.GetFileNameWithoutExtension(file),
            "–",
            "",
            DefaultProfile,
            DefaultPage,
            0,
            1,
            false,
            "",
            "",
            "");

    private static StreamDeckExecutionPolicy DefaultExecutionPolicy() =>
        new(250, 1, 1000);
}
