using System.Text.Json;

namespace CreatorControlSuite.App.Services;

public sealed record TwitchProfessionalHistorySnapshot(
    string TotalStreams,
    string RecordPeak,
    string RecordAverage,
    string TotalDuration,
    string TotalFollowers,
    string ViewerTrend,
    string FollowerTrend,
    string CategoryTrend,
    string DurationTrend,
    string PeakTrend,
    string AverageTrend,
    string ChatRate,
    string BestCategory,
    string EngagementRate,
    string FollowerRate,
    string Consistency,
    string Summary,
    IReadOnlyList<string> HistoryItems);

public static class TwitchProfessionalHistoryService
{
    public static async Task<TwitchProfessionalHistorySnapshot> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string[] lines = File.Exists(path)
            ? await File.ReadAllLinesAsync(path, cancellationToken)
            : [];
        return CreateSnapshot(lines);
    }

    public static TwitchProfessionalHistorySnapshot CreateSnapshot(
        IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<HistoryRow> rows = ParseRows(lines);
        if (rows.Count == 0)
        {
            return Empty();
        }

        List<HistoryRow> recent =
        [
            .. rows.OrderBy(row => row.StartedAt).TakeLast(10)
        ];
        List<HistoryRow> ordered =
        [
            .. rows.OrderByDescending(row => row.StartedAt)
        ];
        List<HistoryRow> latestFive = [.. ordered.Take(5)];
        List<HistoryRow> previousFive =
        [
            .. ordered.Skip(5).Take(5)
        ];

        double latestPeak = AverageOrZero(
            latestFive,
            row => row.Peak);
        double previousPeak = AverageOrZero(
            previousFive,
            row => row.Peak);
        double latestAverage = AverageOrZero(
            latestFive,
            row => row.Average);
        double previousAverage = AverageOrZero(
            previousFive,
            row => row.Average);
        double totalHours = rows.Sum(row => row.DurationSeconds) / 3600d;
        string bestCategory = rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.Category) &&
                row.Category != "-")
            .GroupBy(row => row.Category)
            .Select(group => new
            {
                Name = group.Key,
                Average = group.Average(row => row.Average)
            })
            .OrderByDescending(item => item.Average)
            .FirstOrDefault()?.Name ?? "-";
        string commonCategory = rows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.Category) &&
                row.Category != "-")
            .GroupBy(row => row.Category)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault()?.Key ?? "-";

        (string viewerTrend, string followerTrend) =
            BuildRecentTrends(recent);
        IReadOnlyList<string> historyItems =
        [
            .. ordered.Take(20).Select(FormatHistoryRow)
        ];
        int totalEngagement = rows.Sum(row => row.Chat + row.Events);
        return new TwitchProfessionalHistorySnapshot(
            rows.Count.ToString(),
            rows.Max(row => row.Peak).ToString(),
            rows.Max(row => row.Average).ToString("0.0"),
            StreamStatisticsApplicationService.FormatDuration(
                rows.Sum(row => row.DurationSeconds)),
            rows.Sum(row => row.Followers).ToString(),
            viewerTrend,
            followerTrend,
            "Häufigste Kategorie: " + commonCategory,
            "Ø Streamdauer: " +
            StreamStatisticsApplicationService.FormatDuration(
                (long)rows.Average(row => row.DurationSeconds)),
            PercentTrend(latestPeak, previousPeak),
            PercentTrend(latestAverage, previousAverage),
            FormatRate(rows.Sum(row => row.Chat), totalHours, "0.0"),
            bestCategory,
            FormatRate(totalEngagement, totalHours, "0.0"),
            FormatRate(rows.Sum(row => row.Followers), totalHours, "0.00"),
            DescribeConsistency(latestFive.Select(row => row.Average)),
            $"Letzte {latestFive.Count} Streams: " +
            $"Ø {latestAverage:0.0} Zuschauer, " +
            $"mittlerer Peak {latestPeak:0.0}. Insgesamt " +
            $"{rows.Sum(row => row.Chat)} Chatnachrichten und " +
            $"{rows.Sum(row => row.Followers)} neue Follower.",
            historyItems);
    }

    private static List<HistoryRow> ParseRows(IEnumerable<string> lines)
    {
        var rows = new List<HistoryRow>();
        foreach (string line in lines)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                rows.Add(new HistoryRow(
                    root.GetProperty("StartedAt").GetDateTimeOffset(),
                    GetInt64(root, "DurationSeconds"),
                    GetInt32(root, "PeakViewers"),
                    GetDouble(root, "AverageViewers"),
                    GetInt32(root, "FollowersGained"),
                    GetInt32(root, "ChatMessages"),
                    GetInt32(root, "AlertsPlayed"),
                    GetString(root, "Category", "-"),
                    GetString(root, "Title", "-")));
            }
            catch (JsonException)
            {
                // Beschädigte JSONL-Zeilen dürfen gültige Sessions nicht verbergen.
            }
            catch (InvalidOperationException)
            {
                // Inkompatible ältere Feldtypen werden übersprungen.
            }
            catch (FormatException)
            {
                // Ungültige Datums- oder Zahlenformate werden übersprungen.
            }
            catch (KeyNotFoundException)
            {
                // Eine Zeile ohne Startzeit ist keine verwertbare Session.
            }
        }

        return rows;
    }

    private static TwitchProfessionalHistorySnapshot Empty() =>
        new(
            "0",
            "0",
            "0,0",
            "00:00",
            "0",
            "Zuschauertrend: Noch nicht genügend Daten",
            "Followertrend: Noch nicht genügend Daten",
            "Häufigste Kategorie: -",
            "Ø Streamdauer: 00:00",
            "-",
            "-",
            "0",
            "-",
            "0",
            "0",
            "-",
            "Noch keine Trenddaten verfügbar.",
            ["Noch keine abgeschlossenen Streams gespeichert."]);

    private static (string Viewer, string Followers) BuildRecentTrends(
        IReadOnlyList<HistoryRow> recent)
    {
        if (recent.Count < 2)
        {
            return (
                "Zuschauertrend: Noch nicht genügend Daten",
                "Followertrend: Noch nicht genügend Daten");
        }

        int split = Math.Max(1, recent.Count / 2);
        double earlier = recent.Take(split).Average(row => row.Average);
        double later = recent.Skip(split).Average(row => row.Average);
        double delta = later - earlier;
        return (
            $"Zuschauertrend: {(delta >= 0 ? "+" : "")}" +
            $"{delta:0.0} Ø Zuschauer",
            $"Followertrend: {recent.Average(row => row.Followers):0.0} " +
            "pro Stream");
    }

    private static string DescribeConsistency(IEnumerable<double> values)
    {
        double[] samples = [.. values];
        if (samples.Length < 2 || samples.Average() <= 0)
        {
            return "-";
        }

        double mean = samples.Average();
        double variance = samples.Sum(
            value => Math.Pow(value - mean, 2)) / samples.Length;
        double coefficient = Math.Sqrt(variance) / mean;
        return coefficient switch
        {
            <= 0.15 => "Sehr stabil",
            <= 0.30 => "Stabil",
            <= 0.50 => "Schwankend",
            _ => "Stark schwankend"
        };
    }

    private static string PercentTrend(double current, double previous) =>
        previous <= 0
            ? "-"
            : $"{(current - previous) / previous * 100:+0.0;-0.0;0.0}%";

    private static string FormatRate(
        int total,
        double hours,
        string format) =>
        hours <= 0 ? "0" : (total / hours).ToString(format);

    private static double AverageOrZero(
        IReadOnlyCollection<HistoryRow> rows,
        Func<HistoryRow, double> selector) =>
        rows.Count == 0 ? 0 : rows.Average(selector);

    private static string FormatHistoryRow(HistoryRow row)
    {
        DateTimeOffset local = row.StartedAt.ToLocalTime();
        TimeSpan duration = TimeSpan.FromSeconds(
            Math.Max(0, row.DurationSeconds));
        return $"{local:dd.MM.yyyy HH:mm} · {duration:hh\\:mm\\:ss} · " +
               $"Peak {row.Peak} · Ø {row.Average:0.0} · " +
               $"+{row.Followers} Follower · {row.Category}";
    }

    private static string GetString(
        JsonElement root,
        string name,
        string fallback) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value.GetString() ?? fallback
            : fallback;

    private static int GetInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value.GetInt32()
            : 0;

    private static long GetInt64(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value.GetInt64()
            : 0;

    private static double GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value.GetDouble()
            : 0;

    private sealed record HistoryRow(
        DateTimeOffset StartedAt,
        long DurationSeconds,
        int Peak,
        double Average,
        int Followers,
        int Chat,
        int Events,
        string Category,
        string Title);
}
