// Runs the original C# analysis without starting its background writer or touching user data.
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CreatorControlSuite.App.Services.CreatorIntelligence;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var now = DateTimeOffset.UtcNow;
string root = Path.Combine(Path.GetTempPath(), "ccs-reference-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var events = new List<CreatorIntelligenceEvent>();
    void Add(string id, DateTimeOffset at, string type, object? payload) => events.Add(new(at, id, type, payload is null ? null : JsonSerializer.SerializeToElement(payload)));
    for (int session = 0; session < 12; session++)
    {
        string id = "reference-" + session;
        var start = new DateTimeOffset(now.Date.AddDays(-27 + session * 2).AddHours(16 + session % 3), TimeSpan.Zero);
        Add(id, start, "session.started", new { title = "Session <" + session + ">", category = session % 2 == 0 ? "Gaming" : "Talk" });
        Add(id, start, "obs.scene.changed", new { scene = "Main" });
        Add(id, start, "spotify.track.changed", new { track = "Song", artist = "Artist" });
        for (int minute = 0; minute <= 60; minute += 5)
            Add(id, start.AddMinutes(minute), "twitch.viewer.sample", new { viewers = 10 + session * 3 + (minute < 30 ? minute : 45 - minute), scene = minute < 30 ? "Main" : "Break" });
        for (int message = 0; message < session * 3 + 3; message++)
            Add(id, start.AddMinutes(message % 60), "twitch.chat.message", new { user = "User" });
        for (int follower = 0; follower < session % 4; follower++)
            Add(id, start.AddMinutes(10 + follower), "twitch.follow", new { Summary = "Follow" });
        Add(id, start.AddMinutes(20), "twitch.event", new { type = "channel.raid", summary = "Raid <guest>" });
        Add(id, start.AddMinutes(25), "session.note", new { note = "Interview" });
        Add(id, start.AddMinutes(30), "obs.scene.changed", new { scene = "Break" });
        Add(id, start.AddMinutes(35), "spotify.track.changed", new { track = "Song 2", artist = "Artist" });
        Add(id, start.AddMinutes(65), "session.ended", new { endedAt = start.AddMinutes(65) });
    }
    Add("unfinished", now.AddMinutes(-20), "session.started", new { title = "Live", category = "Talk" });
    Add("unfinished", now.AddMinutes(-10), "twitch.viewer.sample", new { viewers = 5 });
    File.WriteAllLines(Path.Combine(root, "events.jsonl"), events.Select(x => JsonSerializer.Serialize(x)));
    var service = (CreatorIntelligenceService)RuntimeHelpers.GetUninitializedObject(typeof(CreatorIntelligenceService));
    typeof(CreatorIntelligenceService).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, root);
    CreatorActionItem[] actions = [
        new("custom-engagement", "Chat goal", "engagement", 0, 15, 1, "Offen", now.AddDays(-30), null, null),
        new("closed", "Completed score", "score", 10, 20, 2, "Erledigt", now.AddDays(-30), now.AddDays(-20), 11),
        new("declined", "Retention goal", "retention", 100, 120, 2, "Offen", now.AddDays(-30), null, null),
    ];
    CreatorExperiment[] experiments = [
        new("test", "custom-engagement", "Chat test", "engagement", 0, 3, "Aktiv", now.Date.AddDays(-14), null),
        new("future", "custom-engagement", "Not started yet", "score", 20, 3, "Aktiv", now.AddDays(1), null),
    ];
    File.WriteAllText(Path.Combine(root, "action-plan.json"), JsonSerializer.Serialize(actions));
    File.WriteAllText(Path.Combine(root, "experiments.json"), JsonSerializer.Serialize(experiments));
    var output = new
    {
        now,
        offsetSeconds = (int)TimeZoneInfo.Local.GetUtcOffset(now).TotalSeconds,
        events,
        latest = await service.AnalyzeLatestSessionAsync(),
        dashboard = await service.AnalyzeDashboardAsync(),
        content = await service.AnalyzeContentPerformanceAsync(),
        correlation = await service.AnalyzeEventCorrelationsAsync(),
        actionsInput = actions,
        experimentsInput = experiments,
        actions = await service.AnalyzeActionPlanAsync(),
        effectiveness = await service.AnalyzeActionEffectivenessAsync(),
        experiments = await service.AnalyzeExperimentsAsync(),
    };
    Directory.CreateDirectory(Path.GetDirectoryName(args[0])!);
    File.WriteAllText(args[0], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Generated C# reference: {events.Count} events, 12 completed sessions.");
}
finally
{
    string target = Path.GetFullPath(root);
    string tempRoot = Path.GetFullPath(Path.GetTempPath());
    if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("ccs-reference-", StringComparison.Ordinal))
        throw new InvalidOperationException("Reference cleanup target is outside its temporary workspace.");
    Directory.Delete(target, true);
}
