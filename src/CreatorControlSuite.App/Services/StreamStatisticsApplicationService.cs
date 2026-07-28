using System.Text.Json;

namespace CreatorControlSuite.App.Services;

public sealed record StreamStatisticsRow(
    string Date,
    string Duration,
    double AverageViewers,
    int PeakViewers,
    int FollowersGained,
    int NewSubscriptions,
    int GiftSubscriptions,
    int BitsCheered,
    string Category,
    string Title,
    long DurationSeconds,
    DateTimeOffset StartedAt);

public sealed record StreamStatisticsSnapshot(
    string TotalStreams,
    string TotalDuration,
    string AverageViewers,
    string PeakViewers,
    string Followers,
    string AverageDuration,
    IReadOnlyList<StreamStatisticsRow> Rows,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Development);

public sealed class StreamStatisticsApplicationService
{
    public async Task<StreamStatisticsSnapshot> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string[] lines = File.Exists(path)
            ? await File.ReadAllLinesAsync(path, cancellationToken)
            : [];
        return CreateSnapshot(lines);
    }

    public static StreamStatisticsSnapshot CreateSnapshot(
        IEnumerable<string> lines)
    {
        var rows = new List<StreamStatisticsRow>();
        foreach (string line in lines.Where(
                     value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement item = document.RootElement;
                DateTimeOffset startedAt = GetDate(item, "StartedAt");
                long durationSeconds = GetInt64(item, "DurationSeconds");
                double averageViewers = GetDouble(item, "AverageViewers");
                string category = GetString(item, "Category");
                rows.Add(new(
                    startedAt == DateTimeOffset.MinValue
                        ? "-"
                        : startedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                    TimeSpan.FromSeconds(Math.Max(0, durationSeconds))
                        .ToString(@"hh\:mm\:ss"),
                    Math.Round(averageViewers, 1),
                    GetInt32(item, "PeakViewers"),
                    GetInt32(item, "FollowersGained"),
                    GetInt32(item, "NewSubscriptions"),
                    GetInt32(item, "GiftSubscriptions"),
                    GetInt32(item, "BitsCheered"),
                    string.IsNullOrWhiteSpace(category)
                        ? "Nicht angegeben"
                        : category,
                    GetString(item, "Title"),
                    durationSeconds,
                    startedAt == DateTimeOffset.MinValue
                        ? DateTimeOffset.MinValue
                        : startedAt.ToLocalTime()));
            }
            catch (JsonException)
            {
                // A damaged JSONL row must not hide valid stream sessions.
            }
            catch (InvalidOperationException)
            {
                // Ignore fields with incompatible JSON types.
            }
            catch (FormatException)
            {
                // Ignore dates and numbers with incompatible text formats.
            }
        }

        List<StreamStatisticsRow> ordered =
        [
            .. rows.OrderByDescending(row => row.StartedAt)
        ];
        long totalSeconds = rows.Sum(
            row => Math.Max(0, row.DurationSeconds));
        double weightedAverage = totalSeconds > 0
            ? rows.Sum(row =>
                row.AverageViewers *
                Math.Max(0, row.DurationSeconds)) / totalSeconds
            : rows.Count > 0
                ? rows.Average(row => row.AverageViewers)
                : 0;
        IReadOnlyList<string> categories =
        [
            .. rows.GroupBy(
                    row => row.Category,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Name = group.Key,
                    Count = group.Count(),
                    Hours = group.Sum(row => row.DurationSeconds) / 3600.0,
                    Average = group.Average(row => row.AverageViewers)
                })
                .OrderByDescending(item => item.Count)
                .ThenByDescending(item => item.Hours)
                .Select(item =>
                    $"{item.Name} · {item.Count} Stream(s) · " +
                    $"{item.Hours:0.0} h · Ø {item.Average:0.0} Viewer")
        ];
        IReadOnlyList<string> development =
        [
            .. rows.Where(row => row.StartedAt != DateTimeOffset.MinValue)
                .OrderBy(row => row.StartedAt)
                .TakeLast(20)
                .Select(row =>
                    $"{row.StartedAt:dd.MM.} · Ø {row.AverageViewers:0.0} · " +
                    $"Peak {row.PeakViewers} · +{row.FollowersGained} Follower")
        ];

        return new(
            rows.Count.ToString(),
            FormatDuration(totalSeconds),
            weightedAverage.ToString("0.0"),
            (rows.Count == 0 ? 0 : rows.Max(row => row.PeakViewers)).ToString(),
            rows.Sum(row => row.FollowersGained).ToString(),
            FormatDuration(rows.Count == 0 ? 0 : totalSeconds / rows.Count),
            ordered,
            categories.Count == 0
                ? ["Noch keine Kategorien gespeichert."]
                : categories,
            development.Count == 0
                ? ["Noch keine Verlaufsdaten vorhanden."]
                : development);
    }

    public static string FormatDuration(long seconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 24
            ? $"{(int)duration.TotalDays}d {duration.Hours:00}:{duration.Minutes:00}"
            : duration.ToString(@"hh\:mm");
    }

    private static string GetString(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
            ? value.ToString()
            : "";

    private static int GetInt32(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
            ? value.GetInt32()
            : 0;

    private static long GetInt64(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
            ? value.GetInt64()
            : 0;

    private static double GetDouble(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
            ? value.GetDouble()
            : 0;

    private static DateTimeOffset GetDate(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
            ? value.GetDateTimeOffset()
            : DateTimeOffset.MinValue;
}
