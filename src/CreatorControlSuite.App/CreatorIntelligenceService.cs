using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CreatorControlSuite.App;

internal sealed class CreatorIntelligenceService : IAsyncDisposable
{
    private readonly Channel<CreatorIntelligenceEvent> _queue = Channel.CreateBounded<CreatorIntelligenceEvent>(
        new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writerTask;
    private readonly string _root;
    private readonly ConcurrentDictionary<string, CreatorIntelligenceEvent> _latestByType = new(StringComparer.OrdinalIgnoreCase);
    private string? _sessionId;

    public CreatorIntelligenceService()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "CreatorIntelligence");
        Directory.CreateDirectory(_root);
        _writerTask = Task.Run(WriterLoopAsync);
    }

    public bool IsRecording => !string.IsNullOrWhiteSpace(_sessionId);
    public string? SessionId => _sessionId;
    public string RootDirectory => _root;

    public async Task StartSessionAsync(DateTimeOffset startedAt, string? title, string? category, CancellationToken cancellationToken = default)
    {
        if (IsRecording) return;
        _sessionId = $"{startedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        await RecordAsync("session.started", new { startedAt, title, category }, cancellationToken);
    }

    public ValueTask RecordAsync(string type, object? payload = null, CancellationToken cancellationToken = default)
    {
        if (!IsRecording && !string.Equals(type, "session.started", StringComparison.OrdinalIgnoreCase)) return ValueTask.CompletedTask;
        var item = new CreatorIntelligenceEvent(
            DateTimeOffset.UtcNow,
            _sessionId ?? "unassigned",
            type,
            payload is null ? null : JsonSerializer.SerializeToElement(payload));
        _latestByType[type] = item;
        return _queue.Writer.WriteAsync(item, cancellationToken);
    }

    public async Task<CreatorIntelligenceSummary?> CompleteSessionAsync(DateTimeOffset endedAt, CancellationToken cancellationToken = default)
    {
        if (!IsRecording) return null;
        var sessionId = _sessionId!;
        await RecordAsync("session.ended", new { endedAt }, cancellationToken);
        _sessionId = null;
        await FlushAsync(cancellationToken);
        return await AnalyzeSessionAsync(sessionId, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var marker = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FlushWaiters[marker] = completion;
        await _queue.Writer.WriteAsync(new CreatorIntelligenceEvent(DateTimeOffset.UtcNow, "system", "system.flush", JsonSerializer.SerializeToElement(new { marker })), cancellationToken);
        await completion.Task.WaitAsync(cancellationToken);
    }

    private ConcurrentDictionary<string, TaskCompletionSource> FlushWaiters { get; } = new();

    private async Task WriterLoopAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                if (item.Type == "system.flush" && item.Payload is { } flushPayload && flushPayload.TryGetProperty("marker", out var markerElement))
                {
                    var marker = markerElement.GetString();
                    if (marker is not null && FlushWaiters.TryRemove(marker, out var waiter)) waiter.TrySetResult();
                    continue;
                }

                var dayFolder = Path.Combine(_root, item.TimestampUtc.ToLocalTime().ToString("yyyy-MM"));
                Directory.CreateDirectory(dayFolder);
                var path = Path.Combine(dayFolder, "events.jsonl");
                var line = JsonSerializer.Serialize(item);
                await File.AppendAllTextAsync(path, line + Environment.NewLine, new UTF8Encoding(false), _shutdown.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<CreatorIntelligenceDashboard> AnalyzeDashboardAsync(int lookbackDays = 30, CancellationToken cancellationToken = default)
    {
        var allEvents = await ReadAllEventsAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var summaries = allEvents
            .Where(x => x.Type == "session.started" && x.TimestampUtc >= cutoff)
            .Select(x => x.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => allEvents.Any(x => x.SessionId == id && x.Type == "session.ended"))
            .Select(id => BuildSummary(id, allEvents.Where(x => x.SessionId == id).ToList()))
            .OrderBy(x => x.StartedAt)
            .ToList();

        if (summaries.Count == 0)
        {
            return CreatorIntelligenceDashboard.Empty(lookbackDays);
        }

        var recent = summaries.TakeLast(Math.Min(5, summaries.Count)).ToList();
        var previous = summaries.Skip(Math.Max(0, summaries.Count - 10)).Take(Math.Min(5, Math.Max(0, summaries.Count - 5))).ToList();
        var weekCutoff = DateTimeOffset.Now.AddDays(-7);
        var weekly = summaries.Where(x => x.StartedAt >= weekCutoff).ToList();
        var weeklyAverageScore = weekly.Count == 0 ? 0 : weekly.Average(x => x.CreatorScore);
        var averageScore = summaries.Average(x => x.CreatorScore);
        var averageRetention = summaries.Average(x => x.RetentionPercent);
        var averageEngagement = summaries.Average(x => x.ChatMessagesPerHour);
        var averageGrowth = summaries.Average(x => x.FollowersPerHour);
        var averageViewers = summaries.Average(x => x.AverageViewers);
        var recentScore = recent.Average(x => x.CreatorScore);
        var previousScore = previous.Count == 0 ? recentScore : previous.Average(x => x.CreatorScore);
        var scoreTrend = recentScore - previousScore;
        var viewerTrend = LinearTrend(recent.Select(x => x.AverageViewers).ToList());

        var bestHour = summaries.GroupBy(x => x.StartedAt.Hour)
            .Select(g => new { Hour = g.Key, Score = g.Average(x => x.CreatorScore), Count = g.Count() })
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Count).First();
        var bestDay = summaries.GroupBy(x => x.StartedAt.DayOfWeek)
            .Select(g => new { Day = g.Key, Score = g.Average(x => x.CreatorScore), Count = g.Count() })
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Count).First();
        var bestCategory = summaries.Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Category = g.Key, Score = g.Average(x => x.CreatorScore), Viewers = g.Average(x => x.AverageViewers), Count = g.Count() })
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Viewers).FirstOrDefault();

        var predictedViewers = Math.Max(0, recent.Average(x => x.AverageViewers) + viewerTrend);
        var predictedScore = (int)Math.Round(Math.Clamp(recentScore + scoreTrend * .35, 0, 100));
        var qualityIndex = (int)Math.Round(Math.Clamp(averageScore * .55 + Math.Min(averageRetention, 120) / 120 * 45, 0, 100));
        var engagementIndex = (int)Math.Round(Math.Clamp(Math.Min(averageEngagement, 120) / 120 * 70 + Math.Min(averageGrowth, 10) / 10 * 30, 0, 100));
        var growthIndex = (int)Math.Round(Math.Clamp(Math.Min(averageGrowth, 10) / 10 * 65 + Math.Min(Math.Max(viewerTrend, 0), 10) / 10 * 35, 0, 100));

        var insights = new List<string>();
        insights.Add(scoreTrend >= 2
            ? $"Der Creator Score steigt aktuell um {scoreTrend:0.0} Punkte gegenüber dem vorherigen Vergleichszeitraum."
            : scoreTrend <= -2
                ? $"Der Creator Score liegt aktuell {Math.Abs(scoreTrend):0.0} Punkte unter dem vorherigen Vergleichszeitraum."
                : "Der Creator Score ist im Vergleichszeitraum weitgehend stabil.");
        insights.Add($"Die stärkste Startzeit liegt aktuell bei etwa {bestHour.Hour:00}:00 Uhr ({bestDay.Day.ToGermanDayName()}).");
        if (bestCategory is not null) insights.Add($"Die Kategorie „{bestCategory.Category}“ erzielt derzeit die beste Kombination aus Score und Zuschauerzahl.");
        if (averageRetention < 80) insights.Add("Die durchschnittliche Zuschauerbindung ist ausbaufähig. Plane den stärksten Inhalt vor dem typischen Rückgang ein.");
        if (averageEngagement < 12) insights.Add("Mehr direkte Chat-Interaktion könnte den Engagement-Index deutlich verbessern.");
        if (summaries.Count < 5) insights.Add("Für belastbarere Prognosen sollten mindestens fünf vollständige Sessions aufgezeichnet werden.");

        return new CreatorIntelligenceDashboard(
            lookbackDays,
            summaries.Count,
            weekly.Count,
            weeklyAverageScore,
            averageScore,
            qualityIndex,
            engagementIndex,
            growthIndex,
            averageRetention,
            averageEngagement,
            averageGrowth,
            averageViewers,
            scoreTrend,
            viewerTrend,
            bestHour.Hour,
            bestDay.Day,
            bestCategory?.Category ?? "–",
            predictedViewers,
            predictedScore,
            summaries.TakeLast(12).Reverse().ToList(),
            insights);
    }


    public async Task<CreatorContentPerformance> AnalyzeContentPerformanceAsync(int lookbackDays = 30, CancellationToken cancellationToken = default)
    {
        var allEvents = await ReadAllEventsAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var completedSessionIds = allEvents
            .Where(x => x.Type == "session.started" && x.TimestampUtc >= cutoff)
            .Select(x => x.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => allEvents.Any(x => x.SessionId == id && x.Type == "session.ended"))
            .ToHashSet(StringComparer.Ordinal);

        if (completedSessionIds.Count == 0) return CreatorContentPerformance.Empty(lookbackDays);

        var sceneRows = new List<CreatorContentPerformanceRow>();
        var trackRows = new List<CreatorContentPerformanceRow>();
        var heatmap = new Dictionary<(DayOfWeek Day, int Hour), List<double>>();

        foreach (var sessionId in completedSessionIds)
        {
            var events = allEvents.Where(x => x.SessionId == sessionId).OrderBy(x => x.TimestampUtc).ToList();
            var samples = events.Where(x => x.Type == "twitch.viewer.sample" && x.Payload is not null)
                .Select(x => new ViewerPoint(x.TimestampUtc, ReadInt(x.Payload!.Value, "viewers"), ReadString(x.Payload, "scene")))
                .ToList();

            foreach (var sample in samples)
            {
                var local = sample.TimestampUtc.ToLocalTime();
                var key = (local.DayOfWeek, local.Hour);
                if (!heatmap.TryGetValue(key, out var values)) heatmap[key] = values = new List<double>();
                values.Add(sample.Viewers);
            }

            AddSegmentPerformance(sceneRows, events, samples, "obs.scene.changed", "scene", "OBS-Szene");
            AddSegmentPerformance(trackRows, events, samples, "spotify.track.changed", "track", "Spotify-Titel", payload =>
            {
                var title = ReadString(payload, "track");
                var artist = ReadString(payload, "artist");
                if (string.IsNullOrWhiteSpace(title)) title = ReadString(payload, "name");
                return string.IsNullOrWhiteSpace(artist) ? title : $"{title} – {artist}";
            });
        }

        var scenes = AggregatePerformance(sceneRows).Take(12).ToList();
        var tracks = AggregatePerformance(trackRows).Take(12).ToList();
        var heatmapRows = heatmap
            .Select(x => new CreatorHeatmapCell(x.Key.Day, x.Key.Hour, x.Value.Count, x.Value.Average()))
            .OrderByDescending(x => x.AverageViewers)
            .ThenByDescending(x => x.SampleCount)
            .Take(18)
            .ToList();

        var insights = new List<string>();
        var bestScene = scenes.FirstOrDefault();
        if (bestScene is not null) insights.Add($"Die Szene „{bestScene.Name}“ erzielt aktuell die stärkste Zuschauerentwicklung ({FormatSigned(bestScene.ViewerDelta)} Zuschauer je Einsatz).");
        var weakScene = scenes.Where(x => x.Occurrences >= 2).OrderBy(x => x.ViewerDelta).FirstOrDefault();
        if (weakScene is not null && weakScene.ViewerDelta < -1) insights.Add($"Bei „{weakScene.Name}“ sinkt die Zuschauerzahl im Mittel um {Math.Abs(weakScene.ViewerDelta):0.0}. Prüfe Länge, Inhalt und Übergang.");
        var bestTrack = tracks.FirstOrDefault();
        if (bestTrack is not null && bestTrack.ViewerDelta > 0) insights.Add($"Der Titel „{bestTrack.Name}“ war bisher mit der besten Zuschauerentwicklung verbunden.");
        var bestHeat = heatmapRows.FirstOrDefault();
        if (bestHeat is not null) insights.Add($"Das stärkste gemessene Zeitfenster ist {bestHeat.Day.ToGermanDayName()} um {bestHeat.Hour:00}:00 Uhr mit Ø {bestHeat.AverageViewers:0.0} Zuschauern.");
        if (scenes.Count == 0) insights.Add("Für die Szenenanalyse müssen OBS-Szenenwechsel während vollständiger Sessions aufgezeichnet werden.");
        if (tracks.Count == 0) insights.Add("Für die Songanalyse müssen Spotify-Titelwechsel während vollständiger Sessions aufgezeichnet werden.");

        return new CreatorContentPerformance(lookbackDays, completedSessionIds.Count, scenes, tracks, heatmapRows, insights);
    }


    public async Task<CreatorEventCorrelationReport> AnalyzeEventCorrelationsAsync(int lookbackDays = 30, CancellationToken cancellationToken = default)
    {
        var allEvents = await ReadAllEventsAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, lookbackDays));
        var completedSessionIds = allEvents
            .Where(x => x.Type == "session.started" && x.TimestampUtc >= cutoff)
            .Select(x => x.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => allEvents.Any(x => x.SessionId == id && x.Type == "session.ended"))
            .ToHashSet(StringComparer.Ordinal);

        if (completedSessionIds.Count == 0) return CreatorEventCorrelationReport.Empty(lookbackDays);

        var rows = new List<CreatorEventCorrelationRow>();
        var raids = new List<CreatorRaidRetentionRow>();
        foreach (var sessionId in completedSessionIds)
        {
            var events = allEvents.Where(x => x.SessionId == sessionId).OrderBy(x => x.TimestampUtc).ToList();
            var samples = events.Where(x => x.Type == "twitch.viewer.sample")
                .Select(x => new ViewerPoint(x.TimestampUtc, x.Payload is { } p ? ReadInt(p, "viewers") : 0, ReadString(x.Payload, "scene")))
                .OrderBy(x => x.TimestampUtc).ToList();
            if (samples.Count == 0) continue;

            foreach (var evt in events.Where(x => x.Type is "twitch.event" or "session.note" or "obs.scene.changed" or "spotify.track.changed"))
            {
                var eventName = DescribeCorrelationEvent(evt);
                if (string.IsNullOrWhiteSpace(eventName)) continue;
                var before = NearestViewer(samples, evt.TimestampUtc, before: true);
                var after5 = NearestViewer(samples, evt.TimestampUtc.AddMinutes(5), before: false);
                var after10 = NearestViewer(samples, evt.TimestampUtc.AddMinutes(10), before: false);
                if (before is null || after5 is null) continue;
                rows.Add(new CreatorEventCorrelationRow(eventName, evt.Type, 1, before.Value, after5.Value - before.Value, after10 is null ? 0 : after10.Value - before.Value));

                if (evt.Type == "twitch.event" && IsRaidEvent(evt.Payload))
                {
                    var after30 = NearestViewer(samples, evt.TimestampUtc.AddMinutes(30), before: false);
                    raids.Add(new CreatorRaidRetentionRow(
                        ReadString(evt.Payload, "summary"),
                        before.Value,
                        after5.Value,
                        after10 ?? after5.Value,
                        after30 ?? after10 ?? after5.Value));
                }
            }
        }

        var correlations = rows
            .GroupBy(x => $"{x.EventType}\u001f{x.EventName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => new CreatorEventCorrelationRow(g.First().EventName, g.First().EventType, g.Count(), g.Average(x => x.BaselineViewers), g.Average(x => x.ViewerDelta5Minutes), g.Average(x => x.ViewerDelta10Minutes)))
            .OrderByDescending(x => x.ViewerDelta10Minutes)
            .ThenByDescending(x => x.Occurrences)
            .Take(20).ToList();

        var raidRows = raids
            .GroupBy(x => string.IsNullOrWhiteSpace(x.RaidSummary) ? "Raid" : x.RaidSummary, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CreatorRaidRetentionRow(g.Key, g.Average(x => x.ViewersBefore), g.Average(x => x.ViewersAfter5), g.Average(x => x.ViewersAfter10), g.Average(x => x.ViewersAfter30)))
            .OrderByDescending(x => x.Retention30Percent)
            .Take(12).ToList();

        var actions = new List<string>();
        var strongest = correlations.FirstOrDefault(x => x.Occurrences >= 2);
        if (strongest is not null && strongest.ViewerDelta10Minutes > 1)
            actions.Add($"„{strongest.EventName}“ ist nach zehn Minuten im Mittel mit {FormatSigned(strongest.ViewerDelta10Minutes)} Zuschauern verbunden. Diesen Ablauf gezielt wiederholen.");
        var weakest = correlations.Where(x => x.Occurrences >= 2).OrderBy(x => x.ViewerDelta10Minutes).FirstOrDefault();
        if (weakest is not null && weakest.ViewerDelta10Minutes < -1)
            actions.Add($"Nach „{weakest.EventName}“ fehlen nach zehn Minuten durchschnittlich {Math.Abs(weakest.ViewerDelta10Minutes):0.0} Zuschauer. Übergang, Länge oder Inhalt prüfen.");
        var bestRaid = raidRows.FirstOrDefault();
        if (bestRaid is not null)
            actions.Add($"Die beste gemessene Raid-Bindung erreicht „{bestRaid.RaidSummary}“ mit {bestRaid.Retention30Percent:0}% nach 30 Minuten.");
        if (correlations.Count == 0) actions.Add("Noch keine Ereignisse konnten mit ausreichend Zuschauer-Samples korreliert werden.");
        if (raids.Count == 0) actions.Add("Raid-Bindung wird angezeigt, sobald Raid-Events und Zuschauer-Samples gemeinsam aufgezeichnet wurden.");

        return new CreatorEventCorrelationReport(lookbackDays, completedSessionIds.Count, correlations, raidRows, actions);
    }

    public async Task<CreatorActionPlan> AnalyzeActionPlanAsync(CancellationToken cancellationToken = default)
    {
        var dashboard = await AnalyzeDashboardAsync(30, cancellationToken);
        var correlation = await AnalyzeEventCorrelationsAsync(30, cancellationToken);
        var path = Path.Combine(_root, "action-plan.json");
        var stored = new List<CreatorActionItem>();
        if (File.Exists(path))
        {
            try { stored = JsonSerializer.Deserialize<List<CreatorActionItem>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? new(); }
            catch { stored = new(); }
        }

        var suggestions = new List<(string Title, string Metric, double Baseline, double Target, int Priority)>();
        if (dashboard.SessionCount > 0)
        {
            if (dashboard.AverageRetentionPercent < 90)
                suggestions.Add(("Zuschauerbindung auf mindestens 90 % erhöhen", "retention", dashboard.AverageRetentionPercent, 90, 1));
            if (dashboard.AverageChatMessagesPerHour < 15)
                suggestions.Add(("Mindestens 15 Chatnachrichten pro Stunde erreichen", "engagement", dashboard.AverageChatMessagesPerHour, 15, 2));
            if (dashboard.CreatorScoreTrend < 1)
                suggestions.Add(("Creator Score gegenüber dem aktuellen Niveau um 5 Punkte steigern", "score", dashboard.AverageCreatorScore, Math.Min(100, dashboard.AverageCreatorScore + 5), 2));
            if (dashboard.AverageFollowersPerHour < 1)
                suggestions.Add(("Follower-Rate auf mindestens 1 pro Stunde erhöhen", "growth", dashboard.AverageFollowersPerHour, 1, 3));
        }
        foreach (var action in correlation.Actions.Take(3))
            suggestions.Add((action, "manual", 0, 1, 3));

        var now = DateTimeOffset.Now;
        foreach (var suggestion in suggestions)
        {
            if (stored.Any(x => string.Equals(x.Title, suggestion.Title, StringComparison.OrdinalIgnoreCase))) continue;
            stored.Add(new CreatorActionItem(Guid.NewGuid().ToString("N"), suggestion.Title, suggestion.Metric, suggestion.Baseline, suggestion.Target, suggestion.Priority, "Offen", now, null, null));
        }

        double Current(string metric) => metric switch
        {
            "retention" => dashboard.AverageRetentionPercent,
            "engagement" => dashboard.AverageChatMessagesPerHour,
            "score" => dashboard.AverageCreatorScore,
            "growth" => dashboard.AverageFollowersPerHour,
            _ => 0
        };

        stored = stored.Select(item =>
        {
            if (item.Status == "Erledigt" || item.Metric == "manual" || dashboard.SessionCount == 0) return item;
            var current = Current(item.Metric);
            var status = current >= item.Target ? "Automatisch erreicht" : item.Status;
            return item with { CurrentValue = current, Status = status, CompletedAt = status == "Automatisch erreicht" ? now : item.CompletedAt };
        }).OrderBy(x => x.Status is "Erledigt" or "Automatisch erreicht").ThenBy(x => x.Priority).ThenBy(x => x.CreatedAt).ToList();

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
        return new CreatorActionPlan(stored, stored.Count(x => x.Status == "Offen"), stored.Count(x => x.Status is "Erledigt" or "Automatisch erreicht"));
    }

    public async Task CompleteActionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, "action-plan.json");
        if (!File.Exists(path)) return;
        List<CreatorActionItem> items;
        try { items = JsonSerializer.Deserialize<List<CreatorActionItem>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? new(); }
        catch { return; }
        var now = DateTimeOffset.Now;
        items = items.Select(x => x.Id == actionId ? x with { Status = "Erledigt", CompletedAt = now } : x).ToList();
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);
    }

    public async Task<CreatorActionEffectivenessReport> AnalyzeActionEffectivenessAsync(CancellationToken cancellationToken = default)
    {
        var plan = await AnalyzeActionPlanAsync(cancellationToken);
        var rows = plan.Items
            .Where(x => x.Metric != "manual")
            .Select(x =>
            {
                var current = x.CurrentValue ?? x.Baseline;
                var required = Math.Max(0.01, x.Target - x.Baseline);
                var improvement = current - x.Baseline;
                var progress = Math.Clamp(improvement / required * 100, 0, 200);
                var verdict = x.Status is "Erledigt" or "Automatisch erreicht"
                    ? improvement > 0 ? "Ziel erreicht · positive Entwicklung" : "Ziel abgeschlossen · Wirkung noch nicht messbar"
                    : improvement > required * 0.5 ? "Deutliche Verbesserung"
                    : improvement > 0 ? "Leichte Verbesserung"
                    : improvement < 0 ? "Wert hat sich verschlechtert"
                    : "Noch keine messbare Veränderung";
                return new CreatorActionEffectivenessRow(x.Id, x.Title, x.Metric, x.Status, x.Baseline, current, x.Target, improvement, progress, verdict, x.CreatedAt, x.CompletedAt);
            })
            .OrderByDescending(x => x.Status is "Erledigt" or "Automatisch erreicht")
            .ThenByDescending(x => x.ProgressPercent)
            .ThenByDescending(x => x.Improvement)
            .ToList();

        var improved = rows.Count(x => x.Improvement > 0.01);
        var declined = rows.Count(x => x.Improvement < -0.01);
        var reached = rows.Count(x => x.Status is "Erledigt" or "Automatisch erreicht");
        var strongest = rows.Where(x => x.Improvement > 0.01).OrderByDescending(x => x.ProgressPercent).FirstOrDefault();
        var summary = strongest is null
            ? "Noch keine Maßnahme zeigt eine belastbare positive Veränderung."
            : $"Stärkste beobachtete Entwicklung: {strongest.Title} ({FormatSigned(strongest.Improvement)}; {strongest.ProgressPercent:0}% des Zielwegs).";

        return new CreatorActionEffectivenessReport(rows, improved, declined, reached, summary);
    }


    public async Task StartExperimentFromActionAsync(string actionId, CancellationToken cancellationToken = default)
    {
        var plan = await AnalyzeActionPlanAsync(cancellationToken);
        var action = plan.Items.FirstOrDefault(x => x.Id == actionId);
        if (action is null || action.Metric == "manual") return;

        var path = Path.Combine(_root, "experiments.json");
        var items = await ReadExperimentsAsync(path, cancellationToken);
        if (items.Any(x => x.Status == "Aktiv" && string.Equals(x.ActionId, actionId, StringComparison.Ordinal))) return;

        items.Add(new CreatorExperiment(
            Guid.NewGuid().ToString("N"),
            action.Id,
            action.Title,
            action.Metric,
            action.Baseline,
            3,
            "Aktiv",
            DateTimeOffset.Now,
            null));
        await WriteExperimentsAsync(path, items, cancellationToken);
    }

    public async Task<CreatorExperimentReport> AnalyzeExperimentsAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, "experiments.json");
        var experiments = await ReadExperimentsAsync(path, cancellationToken);
        var events = await ReadAllEventsAsync(cancellationToken);
        var summaries = events
            .Where(x => x.Type == "session.started")
            .Select(x => x.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Where(id => events.Any(x => x.SessionId == id && x.Type == "session.ended"))
            .Select(id => BuildSummary(id, events.Where(x => x.SessionId == id).ToList()))
            .OrderBy(x => x.StartedAt)
            .ToList();

        double MetricValue(CreatorIntelligenceSummary x, string metric) => metric switch
        {
            "retention" => x.RetentionPercent,
            "engagement" => x.ChatMessagesPerHour,
            "score" => x.CreatorScore,
            "growth" => x.FollowersPerHour,
            _ => 0
        };

        var now = DateTimeOffset.Now;
        var rows = new List<CreatorExperimentRow>();
        var updated = new List<CreatorExperiment>();
        foreach (var experiment in experiments)
        {
            var before = summaries.Where(x => x.StartedAt < experiment.StartedAt).TakeLast(3).ToList();
            var during = summaries.Where(x => x.StartedAt >= experiment.StartedAt && (experiment.CompletedAt is null || x.StartedAt <= experiment.CompletedAt.Value)).Take(experiment.TargetSessions).ToList();
            var baseline = before.Count == 0 ? experiment.Baseline : before.Average(x => MetricValue(x, experiment.Metric));
            var current = during.Count == 0 ? baseline : during.Average(x => MetricValue(x, experiment.Metric));
            var delta = current - baseline;
            var status = experiment.Status;
            DateTimeOffset? completedAt = experiment.CompletedAt;
            if (status == "Aktiv" && during.Count >= experiment.TargetSessions)
            {
                status = "Ausgewertet";
                completedAt = now;
            }
            var confidence = during.Count >= 3 && before.Count >= 3 ? "Mittel" : during.Count >= 2 ? "Vorläufig" : "Zu wenig Daten";
            var verdict = during.Count == 0
                ? "Noch kein vollständiger Stream seit Teststart."
                : delta > Math.Max(0.5, Math.Abs(baseline) * 0.05)
                    ? "Positive Veränderung beobachtet."
                    : delta < -Math.Max(0.5, Math.Abs(baseline) * 0.05)
                        ? "Negative Veränderung beobachtet."
                        : "Noch kein klarer Unterschied erkennbar.";
            updated.Add(experiment with { Status = status, CompletedAt = completedAt });
            rows.Add(new CreatorExperimentRow(experiment.Id, experiment.ActionId, experiment.Title, experiment.Metric, status, baseline, current, delta, during.Count, experiment.TargetSessions, confidence, verdict, experiment.StartedAt, completedAt));
        }
        await WriteExperimentsAsync(path, updated, cancellationToken);

        var completed = rows.Count(x => x.Status == "Ausgewertet");
        var positive = rows.Count(x => x.Delta > Math.Max(0.5, Math.Abs(x.Baseline) * 0.05));
        var active = rows.Count(x => x.Status == "Aktiv");
        var summary = rows.Count == 0
            ? "Noch keine Experimente gestartet. Wähle eine messbare Maßnahme aus und starte daraus einen Test."
            : $"{active} aktiv · {completed} ausgewertet · {positive} mit positiver beobachteter Veränderung.";
        return new CreatorExperimentReport(rows.OrderByDescending(x => x.Status == "Aktiv").ThenByDescending(x => x.StartedAt).ToList(), active, completed, positive, summary);
    }

    private static async Task<List<CreatorExperiment>> ReadExperimentsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<CreatorExperiment>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? new(); }
        catch { return new(); }
    }

    private static Task WriteExperimentsAsync(string path, List<CreatorExperiment> items, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken);

    public async Task<string> GenerateWeeklyReportAsync(CancellationToken cancellationToken = default)
    {
        var dashboard = await AnalyzeDashboardAsync(7, cancellationToken);
        var content = await AnalyzeContentPerformanceAsync(7, cancellationToken);
        var correlation = await AnalyzeEventCorrelationsAsync(7, cancellationToken);
        var now = DateTimeOffset.Now;
        var reportFolder = Path.Combine(_root, "Reports");
        Directory.CreateDirectory(reportFolder);
        var path = Path.Combine(reportFolder, $"creator-weekly-{now:yyyy-MM-dd-HHmm}.html");
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>Creator Intelligence Wochenbericht</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial;background:#0b1014;color:#eef3f6;margin:32px}section{background:#11191f;border:1px solid #29343c;border-radius:12px;padding:18px;margin:14px 0}h1,h2{margin-top:0}.metric{display:inline-block;min-width:150px;margin:8px;padding:12px;background:#172129;border-radius:8px}.muted{color:#9fb0bc}li{margin:7px 0}</style></head><body>");
        html.AppendLine($"<h1>Creator Intelligence Wochenbericht</h1><p class=\"muted\">Erstellt am {now:dd.MM.yyyy HH:mm}</p>");
        html.AppendLine("<section><h2>Kennzahlen</h2>");
        html.AppendLine($"<div class=\"metric\"><b>Streams</b><br>{dashboard.SessionCount}</div><div class=\"metric\"><b>Creator Score</b><br>{dashboard.AverageCreatorScore:0.0}</div><div class=\"metric\"><b>Ø Zuschauer</b><br>{dashboard.AverageViewers:0.0}</div><div class=\"metric\"><b>Bindung</b><br>{dashboard.AverageRetentionPercent:0}%</div></section>");
        html.AppendLine("<section><h2>Empfehlungen</h2><ul>");
        foreach (var insight in dashboard.Insights.Concat(content.Insights).Concat(correlation.Actions).Distinct()) html.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(insight)}</li>");
        html.AppendLine("</ul></section><section><h2>Stärkste Szenen</h2><ul>");
        foreach (var scene in content.Scenes.Take(8)) html.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(scene.Name)} · {FormatSigned(scene.ViewerDelta)} Zuschauer · Ø {scene.AverageViewers:0.0}</li>");
        html.AppendLine("</ul></section><section><h2>Ereigniswirkung</h2><ul>");
        foreach (var row in correlation.Correlations.Take(10)) html.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(row.EventName)} · nach 5 Min {FormatSigned(row.ViewerDelta5Minutes)} · nach 10 Min {FormatSigned(row.ViewerDelta10Minutes)}</li>");
        html.AppendLine("</ul></section></body></html>");
        await File.WriteAllTextAsync(path, html.ToString(), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static string DescribeCorrelationEvent(CreatorIntelligenceEvent evt)
    {
        if (evt.Type == "obs.scene.changed") return "OBS-Szene: " + ReadString(evt.Payload, "scene");
        if (evt.Type == "spotify.track.changed")
        {
            var track = ReadString(evt.Payload, "track");
            var artist = ReadString(evt.Payload, "artist");
            return string.IsNullOrWhiteSpace(artist) ? "Spotify: " + track : $"Spotify: {track} – {artist}";
        }
        if (evt.Type == "session.note") return "Notiz: " + ReadString(evt.Payload, "note");
        if (evt.Type == "twitch.event") return ReadString(evt.Payload, "summary");
        return string.Empty;
    }

    private static bool IsRaidEvent(JsonElement? payload)
    {
        var type = ReadString(payload, "type");
        var summary = ReadString(payload, "summary");
        return type.Contains("raid", StringComparison.OrdinalIgnoreCase) || summary.Contains("raid", StringComparison.OrdinalIgnoreCase);
    }

    private static int? NearestViewer(IReadOnlyList<ViewerPoint> samples, DateTimeOffset target, bool before)
    {
        var candidates = before ? samples.Where(x => x.TimestampUtc <= target) : samples.Where(x => x.TimestampUtc >= target);
        var selected = before ? candidates.OrderByDescending(x => x.TimestampUtc).FirstOrDefault() : candidates.OrderBy(x => x.TimestampUtc).FirstOrDefault();
        if (selected is null || Math.Abs((selected.TimestampUtc - target).TotalMinutes) > 12) return null;
        return selected.Viewers;
    }

    private static void AddSegmentPerformance(
        List<CreatorContentPerformanceRow> target,
        List<CreatorIntelligenceEvent> events,
        List<ViewerPoint> samples,
        string eventType,
        string payloadName,
        string kind,
        Func<JsonElement?, string>? nameSelector = null)
    {
        var changes = events.Where(x => x.Type == eventType).OrderBy(x => x.TimestampUtc).ToList();
        for (var index = 0; index < changes.Count; index++)
        {
            var current = changes[index];
            var name = nameSelector?.Invoke(current.Payload) ?? ReadString(current.Payload, payloadName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var end = index + 1 < changes.Count ? changes[index + 1].TimestampUtc : events.Last().TimestampUtc;
            var segmentSamples = samples.Where(x => x.TimestampUtc >= current.TimestampUtc && x.TimestampUtc < end).ToList();
            if (segmentSamples.Count == 0) continue;
            var first = segmentSamples.First().Viewers;
            var last = segmentSamples.Last().Viewers;
            var chats = events.Count(x => x.Type == "twitch.chat.message" && x.TimestampUtc >= current.TimestampUtc && x.TimestampUtc < end);
            var duration = Math.Max((end - current.TimestampUtc).TotalMinutes, 0.1);
            target.Add(new CreatorContentPerformanceRow(kind, name, 1, duration, segmentSamples.Average(x => x.Viewers), last - first, chats / duration));
        }
    }

    private static IEnumerable<CreatorContentPerformanceRow> AggregatePerformance(IEnumerable<CreatorContentPerformanceRow> rows) => rows
        .GroupBy(x => $"{x.Kind}\u001f{x.Name}", StringComparer.OrdinalIgnoreCase)
        .Select(g => new CreatorContentPerformanceRow(
            g.First().Kind,
            g.First().Name,
            g.Sum(x => x.Occurrences),
            g.Sum(x => x.TotalMinutes),
            g.Average(x => x.AverageViewers),
            g.Average(x => x.ViewerDelta),
            g.Average(x => x.ChatMessagesPerMinute)))
        .OrderByDescending(x => x.ViewerDelta)
        .ThenByDescending(x => x.AverageViewers)
        .ThenByDescending(x => x.Occurrences);

    private static string FormatSigned(double value) => value > 0 ? $"+{value:0.0}" : $"{value:0.0}";

    private static double LinearTrend(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var n = values.Count;
        var sumX = (n - 1) * n / 2d;
        var sumY = values.Sum();
        var sumXY = values.Select((value, index) => value * index).Sum();
        var sumXX = Enumerable.Range(0, n).Sum(index => index * index);
        var denominator = n * sumXX - sumX * sumX;
        return Math.Abs(denominator) < .0001 ? 0 : (n * sumXY - sumX * sumY) / denominator;
    }

    public async Task<CreatorIntelligenceSummary?> AnalyzeLatestSessionAsync(CancellationToken cancellationToken = default)
    {
        var events = await ReadAllEventsAsync(cancellationToken);
        var sessionId = events.Where(x => x.Type == "session.started").OrderByDescending(x => x.TimestampUtc).Select(x => x.SessionId).FirstOrDefault();
        return sessionId is null ? null : BuildSummary(sessionId, events.Where(x => x.SessionId == sessionId).ToList());
    }

    private async Task<CreatorIntelligenceSummary?> AnalyzeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var events = (await ReadAllEventsAsync(cancellationToken)).Where(x => x.SessionId == sessionId).ToList();
        return events.Count == 0 ? null : BuildSummary(sessionId, events);
    }

    private async Task<List<CreatorIntelligenceEvent>> ReadAllEventsAsync(CancellationToken cancellationToken)
    {
        var result = new List<CreatorIntelligenceEvent>();
        foreach (var file in Directory.EnumerateFiles(_root, "events.jsonl", SearchOption.AllDirectories).OrderBy(x => x))
        {
            foreach (var line in await File.ReadAllLinesAsync(file, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<CreatorIntelligenceEvent>(line);
                    if (item is not null) result.Add(item);
                }
                catch { }
            }
        }
        return result;
    }

    private static CreatorIntelligenceSummary BuildSummary(string sessionId, List<CreatorIntelligenceEvent> events)
    {
        var ordered = events.OrderBy(x => x.TimestampUtc).ToList();
        var viewers = ordered.Where(x => x.Type == "twitch.viewer.sample" && x.Payload is not null)
            .Select(x => new { Event = x, Count = ReadInt(x.Payload!.Value, "viewers") }).ToList();
        var chats = ordered.Count(x => x.Type == "twitch.chat.message");
        var follows = ordered.Count(x => x.Type == "twitch.follow");
        var scenes = ordered.Where(x => x.Type == "obs.scene.changed").Select(x => ReadString(x.Payload, "scene")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var songs = ordered.Where(x => x.Type == "spotify.track.changed").Count();
        var start = ordered.FirstOrDefault(x => x.Type == "session.started")?.TimestampUtc ?? ordered.First().TimestampUtc;
        var end = ordered.LastOrDefault(x => x.Type == "session.ended")?.TimestampUtc ?? ordered.Last().TimestampUtc;
        var duration = end - start;
        var durationHours = Math.Max(duration.TotalHours, 1d / 60d);
        var startedPayload = ordered.FirstOrDefault(x => x.Type == "session.started")?.Payload;
        var title = ReadString(startedPayload, "title");
        var category = ReadString(startedPayload, "category");
        var peak = viewers.Count == 0 ? 0 : viewers.Max(x => x.Count);
        var average = viewers.Count == 0 ? 0 : viewers.Average(x => x.Count);
        var first = viewers.Take(Math.Max(1, viewers.Count / 3)).Select(x => x.Count).DefaultIfEmpty(0).Average();
        var last = viewers.TakeLast(Math.Max(1, viewers.Count / 3)).Select(x => x.Count).DefaultIfEmpty(0).Average();
        var retention = first <= 0 ? 100 : Math.Clamp(last / first * 100, 0, 200);
        var engagement = chats / durationHours;
        var growth = follows / durationHours;
        var score = (int)Math.Round(Math.Clamp(retention * .35 + Math.Min(engagement, 120) / 120 * 35 + Math.Min(growth, 10) / 10 * 20 + Math.Min(viewers.Count, 20) / 20d * 10, 0, 100));

        var recommendations = new List<string>();
        if (viewers.Count < 3) recommendations.Add("Mehr Zuschauer-Messpunkte sammeln; für belastbare Trends werden mindestens drei Live-Samples benötigt.");
        if (retention < 75) recommendations.Add("Die Zuschauerbindung fällt zum Streamende deutlich ab. Plane vor dem typischen Einbruch einen Szenen-, Kategorie- oder Content-Wechsel.");
        else if (retention > 110) recommendations.Add("Die Zuschauerzahl wächst im letzten Streamdrittel. Der dortige Inhalt sollte künftig früher oder häufiger eingesetzt werden.");
        if (engagement < 10) recommendations.Add("Die Chataktivität ist niedrig. Direkte Fragen, Abstimmungen oder Channel-Point-Aktionen können das Engagement erhöhen.");
        if (scenes <= 1) recommendations.Add("Es wurde kaum zwischen OBS-Szenen gewechselt. Mehr visuelle Abwechslung kann längere Streams strukturieren.");
        if (recommendations.Count == 0) recommendations.Add("Der Stream zeigt stabile Kennzahlen. Vergleiche als Nächstes Kategorien, Startzeiten und Szenen über mehrere Sessions.");

        return new CreatorIntelligenceSummary(sessionId, start.ToLocalTime(), end.ToLocalTime(), title, category, duration, score, peak, average, retention, engagement, growth, chats, follows, scenes, songs, recommendations);
    }

    private static int ReadInt(JsonElement payload, string name) => payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static string ReadString(JsonElement? payload, string name) => payload is { } p && p.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _shutdown.CancelAfter(TimeSpan.FromSeconds(2));
        try { await _writerTask; } catch { }
        _shutdown.Dispose();
    }
}


internal sealed record ViewerPoint(DateTimeOffset TimestampUtc, int Viewers, string Scene);
internal sealed record CreatorContentPerformanceRow(
    string Kind,
    string Name,
    int Occurrences,
    double TotalMinutes,
    double AverageViewers,
    double ViewerDelta,
    double ChatMessagesPerMinute);
internal sealed record CreatorHeatmapCell(DayOfWeek Day, int Hour, int SampleCount, double AverageViewers);
internal sealed record CreatorContentPerformance(
    int LookbackDays,
    int SessionCount,
    IReadOnlyList<CreatorContentPerformanceRow> Scenes,
    IReadOnlyList<CreatorContentPerformanceRow> Tracks,
    IReadOnlyList<CreatorHeatmapCell> Heatmap,
    IReadOnlyList<string> Insights)
{
    public static CreatorContentPerformance Empty(int lookbackDays) => new(
        lookbackDays, 0,
        Array.Empty<CreatorContentPerformanceRow>(),
        Array.Empty<CreatorContentPerformanceRow>(),
        Array.Empty<CreatorHeatmapCell>(),
        new[] { "Noch keine vollständigen Sessions für die Inhaltsanalyse vorhanden." });
}


internal sealed record CreatorEventCorrelationRow(
    string EventName,
    string EventType,
    int Occurrences,
    double BaselineViewers,
    double ViewerDelta5Minutes,
    double ViewerDelta10Minutes);
internal sealed record CreatorRaidRetentionRow(
    string RaidSummary,
    double ViewersBefore,
    double ViewersAfter5,
    double ViewersAfter10,
    double ViewersAfter30)
{
    public double Retention30Percent => ViewersAfter5 <= 0 ? 0 : Math.Clamp(ViewersAfter30 / ViewersAfter5 * 100, 0, 250);
}
internal sealed record CreatorEventCorrelationReport(
    int LookbackDays,
    int SessionCount,
    IReadOnlyList<CreatorEventCorrelationRow> Correlations,
    IReadOnlyList<CreatorRaidRetentionRow> Raids,
    IReadOnlyList<string> Actions)
{
    public static CreatorEventCorrelationReport Empty(int lookbackDays) => new(
        lookbackDays, 0,
        Array.Empty<CreatorEventCorrelationRow>(),
        Array.Empty<CreatorRaidRetentionRow>(),
        new[] { "Noch keine vollständigen Sessions für die Ereigniskorrelation vorhanden." });
}

internal sealed record CreatorActionItem(
    string Id,
    string Title,
    string Metric,
    double Baseline,
    double Target,
    int Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    double? CurrentValue);
internal sealed record CreatorActionPlan(IReadOnlyList<CreatorActionItem> Items, int OpenCount, int CompletedCount);
internal sealed record CreatorActionEffectivenessRow(
    string Id,
    string Title,
    string Metric,
    string Status,
    double Baseline,
    double Current,
    double Target,
    double Improvement,
    double ProgressPercent,
    string Verdict,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
internal sealed record CreatorActionEffectivenessReport(
    IReadOnlyList<CreatorActionEffectivenessRow> Rows,
    int ImprovedCount,
    int DeclinedCount,
    int ReachedCount,
    string Summary);

internal sealed record CreatorIntelligenceEvent(DateTimeOffset TimestampUtc, string SessionId, string Type, JsonElement? Payload);

internal sealed record CreatorExperiment(
    string Id,
    string ActionId,
    string Title,
    string Metric,
    double Baseline,
    int TargetSessions,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

internal sealed record CreatorExperimentRow(
    string Id,
    string ActionId,
    string Title,
    string Metric,
    string Status,
    double Baseline,
    double Current,
    double Delta,
    int SessionCount,
    int TargetSessions,
    string Confidence,
    string Verdict,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

internal sealed record CreatorExperimentReport(
    IReadOnlyList<CreatorExperimentRow> Rows,
    int ActiveCount,
    int CompletedCount,
    int PositiveCount,
    string Summary);

internal sealed record CreatorIntelligenceSummary(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Title,
    string Category,
    TimeSpan Duration,
    int CreatorScore,
    int PeakViewers,
    double AverageViewers,
    double RetentionPercent,
    double ChatMessagesPerHour,
    double FollowersPerHour,
    int ChatMessages,
    int Followers,
    int DistinctScenes,
    int TracksPlayed,
    IReadOnlyList<string> Recommendations);

internal sealed record CreatorIntelligenceDashboard(
    int LookbackDays,
    int SessionCount,
    int WeeklySessionCount,
    double WeeklyAverageCreatorScore,
    double AverageCreatorScore,
    int StreamQualityIndex,
    int EngagementIndex,
    int GrowthIndex,
    double AverageRetentionPercent,
    double AverageChatMessagesPerHour,
    double AverageFollowersPerHour,
    double AverageViewers,
    double CreatorScoreTrend,
    double ViewerTrendPerStream,
    int BestStartHour,
    DayOfWeek BestDay,
    string BestCategory,
    double PredictedAverageViewers,
    int PredictedCreatorScore,
    IReadOnlyList<CreatorIntelligenceSummary> RecentSessions,
    IReadOnlyList<string> Insights)
{
    public static CreatorIntelligenceDashboard Empty(int lookbackDays) => new(
        lookbackDays, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DayOfWeek.Monday, "–", 0, 0,
        Array.Empty<CreatorIntelligenceSummary>(),
        new[] { "Noch keine vollständigen Sessions im gewählten Zeitraum vorhanden." });
}

internal static class CreatorIntelligenceFormattingExtensions
{
    public static string ToGermanDayName(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        DayOfWeek.Sunday => "Sonntag",
        _ => day.ToString()
    };
}
