using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.Tests;

public sealed class StreamDeckCatalogApplicationServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CreatorControlSuite.Tests",
        Guid.NewGuid().ToString("N"));

    public StreamDeckCatalogApplicationServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ReadMetadata_UsesSidecarValuesAndClampsExecutionPolicy()
    {
        string commandFile = CreateAction(
            "scene.cmd",
            """
            {
              "title": "Szene",
              "command": "obs.scene",
              "parameter": "Gaming",
              "profile": "Live",
              "page": "OBS",
              "slot": 7,
              "steps": [{}, {}],
              "locked": true,
              "condition": "stream.live",
              "trueLabel": "LIVE",
              "falseLabel": "OFF",
              "toggleMode": true,
              "alternateCommand": "obs.stop",
              "alternateParameter": "now",
              "stepDelayMs": 20000,
              "retryCount": -1,
              "cooldownMs": 90000
            }
            """);

        StreamDeckCatalogEntry entry =
            StreamDeckCatalogApplicationService.ReadMetadata(commandFile);
        StreamDeckToggleMetadata toggle =
            StreamDeckCatalogApplicationService.ReadToggleMetadata(commandFile);
        StreamDeckExecutionPolicy policy =
            StreamDeckCatalogApplicationService.ReadExecutionPolicy(commandFile);

        Assert.Equal("Szene", entry.Title);
        Assert.Equal("Live", entry.Profile);
        Assert.Equal("OBS", entry.Page);
        Assert.Equal(7, entry.Slot);
        Assert.Equal(2, entry.Steps);
        Assert.True(entry.Locked);
        Assert.True(toggle.ToggleMode);
        Assert.Equal("obs.stop", toggle.AlternateCommand);
        Assert.Equal(new StreamDeckExecutionPolicy(10000, 0, 60000), policy);
    }

    [Fact]
    public void ReadMetadata_FallsBackForMissingOrInvalidSidecar()
    {
        string missing = Path.Combine(_directory, "missing.cmd");
        File.WriteAllText(missing, string.Empty);
        string invalid = Path.Combine(_directory, "invalid.cmd");
        File.WriteAllText(invalid, string.Empty);
        File.WriteAllText(Path.ChangeExtension(invalid, ".json"), "{");

        StreamDeckCatalogEntry first =
            StreamDeckCatalogApplicationService.ReadMetadata(missing);
        StreamDeckCatalogEntry second =
            StreamDeckCatalogApplicationService.ReadMetadata(invalid);

        Assert.Equal(("missing", "Standard", "Hauptseite", 0), (
            first.Title,
            first.Profile,
            first.Page,
            first.Slot));
        Assert.Equal("invalid", second.Title);
        Assert.Equal(
            new StreamDeckExecutionPolicy(250, 1, 1000),
            StreamDeckCatalogApplicationService.ReadExecutionPolicy(invalid));
    }

    [Fact]
    public void ProjectCatalog_FiltersSortsAndSummarizesOccupancy()
    {
        StreamDeckCatalogEntry[] entries =
        [
            Entry("b", "B", "Live", "OBS", 2),
            Entry("a", "A", "live", "OBS", 1),
            Entry("c", "C", "Live", "OBS", 2),
            Entry("d", "D", "Podcast", "Audio", 1)
        ];

        StreamDeckCatalogProjection projection =
            StreamDeckCatalogApplicationService.ProjectCatalog(
                entries,
                selectedProfile: "LIVE",
                selectedPage: "obs");

        Assert.Equal(["Live", "Podcast"], projection.Profiles);
        Assert.Equal(["OBS"], projection.Pages);
        Assert.Equal(["A", "B", "C"], projection.Entries.Select(x => x.Title));
        Assert.Equal(3, projection.OccupiedPositions);
        Assert.Equal(1, projection.Conflicts);
    }

    [Fact]
    public void ResolveDisplayTitleAndFirstFreeSlot_AreStateAware()
    {
        StreamDeckCatalogEntry entry = Entry(
            "a",
            "Stream",
            "Live",
            "Main",
            1,
            condition: "stream.live",
            trueLabel: "LIVE",
            falseLabel: "OFF");

        Assert.Equal(
            "LIVE",
            StreamDeckCatalogApplicationService.ResolveDisplayTitle(
                entry,
                new Dictionary<string, bool> { ["stream.live"] = true }));
        Assert.Equal(
            "Stream",
            StreamDeckCatalogApplicationService.ResolveDisplayTitle(
                entry,
                new Dictionary<string, bool>()));
        Assert.Equal(
            3,
            StreamDeckCatalogApplicationService.FindFirstFreeSlot(
                [entry, Entry("b", "B", "Live", "Main", 2)],
                "live",
                "main",
                excludedFile: null));
    }

    [Fact]
    public void CompareProfiles_ReportsAdditionsAndMissingActions()
    {
        StreamDeckCatalogEntry[] entries =
        [
            Entry("a", "A", "Base", "Main", 1, command: "one"),
            Entry("b", "B", "Base", "Main", 2, command: "two"),
            Entry("c", "C", "Copy", "Main", 1, command: "one"),
            Entry("d", "D", "Copy", "Main", 3, command: "three")
        ];

        IReadOnlyList<string> comparison =
            StreamDeckCatalogApplicationService.CompareProfiles(entries);

        Assert.Equal("Vergleichsbasis: Base", comparison[0]);
        Assert.Equal(
            "Copy: 2 Tasten · +1 hinzugefügt · -1 fehlend",
            comparison[1]);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string CreateAction(string name, string metadata)
    {
        string commandFile = Path.Combine(_directory, name);
        File.WriteAllText(commandFile, string.Empty);
        File.WriteAllText(Path.ChangeExtension(commandFile, ".json"), metadata);
        return commandFile;
    }

    private static StreamDeckCatalogEntry Entry(
        string file,
        string title,
        string profile,
        string page,
        int slot,
        string command = "command",
        string condition = "",
        string trueLabel = "",
        string falseLabel = "") =>
        new(
            file,
            title,
            command,
            "",
            profile,
            page,
            slot,
            1,
            false,
            condition,
            trueLabel,
            falseLabel);
}
