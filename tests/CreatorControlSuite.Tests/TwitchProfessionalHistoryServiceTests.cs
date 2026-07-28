using System.Text.Json;
using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.Tests;

public sealed class TwitchProfessionalHistoryServiceTests
{
    [Fact]
    public void CreateSnapshot_ReturnsStableEmptyProjection()
    {
        TwitchProfessionalHistorySnapshot snapshot =
            TwitchProfessionalHistoryService.CreateSnapshot([]);

        Assert.Equal("0", snapshot.TotalStreams);
        Assert.Equal("00:00", snapshot.TotalDuration);
        Assert.Equal("-", snapshot.PeakTrend);
        Assert.Equal("-", snapshot.Consistency);
        Assert.Equal("Noch keine Trenddaten verfügbar.", snapshot.Summary);
        Assert.Equal(
            ["Noch keine abgeschlossenen Streams gespeichert."],
            snapshot.HistoryItems);
    }

    [Fact]
    public void CreateSnapshot_IgnoresDamagedRowsAndCalculatesTrends()
    {
        string[] lines =
        [
            Row(
                new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero),
                peak: 10,
                average: 5,
                followers: 2,
                chat: 60,
                events: 3,
                category: "Game A"),
            "{damaged",
            Row(
                new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero),
                peak: 20,
                average: 15,
                followers: 4,
                chat: 120,
                events: 5,
                category: "Game B")
        ];

        TwitchProfessionalHistorySnapshot snapshot =
            TwitchProfessionalHistoryService.CreateSnapshot(lines);

        Assert.Equal("2", snapshot.TotalStreams);
        Assert.Equal("20", snapshot.RecordPeak);
        Assert.Equal("15,0", snapshot.RecordAverage);
        Assert.Equal("02:00", snapshot.TotalDuration);
        Assert.Equal("6", snapshot.TotalFollowers);
        Assert.Equal("Zuschauertrend: +10,0 Ø Zuschauer", snapshot.ViewerTrend);
        Assert.Equal("Followertrend: 3,0 pro Stream", snapshot.FollowerTrend);
        Assert.Equal("Häufigste Kategorie: Game A", snapshot.CategoryTrend);
        Assert.Equal("Ø Streamdauer: 01:00", snapshot.DurationTrend);
        Assert.Equal("90,0", snapshot.ChatRate);
        Assert.Equal("Game B", snapshot.BestCategory);
        Assert.Equal("94,0", snapshot.EngagementRate);
        Assert.Equal("3,00", snapshot.FollowerRate);
        Assert.Equal("Schwankend", snapshot.Consistency);
        Assert.Equal(2, snapshot.HistoryItems.Count);
        Assert.Contains("Game B", snapshot.HistoryItems[0]);
    }

    [Fact]
    public void CreateSnapshot_CalculatesFiveStreamComparison()
    {
        string[] lines =
        [
            .. Enumerable.Range(1, 10).Select(index => Row(
                new DateTimeOffset(
                    2026,
                    7,
                    index,
                    18,
                    0,
                    0,
                    TimeSpan.Zero),
                peak: index <= 5 ? 10 : 20,
                average: index <= 5 ? 5 : 10,
                followers: 1,
                chat: 10,
                events: 0,
                category: "Game"))
        ];

        TwitchProfessionalHistorySnapshot snapshot =
            TwitchProfessionalHistoryService.CreateSnapshot(lines);

        Assert.Equal("+100,0%", snapshot.PeakTrend);
        Assert.Equal("+100,0%", snapshot.AverageTrend);
    }

    private static string Row(
        DateTimeOffset startedAt,
        int peak,
        double average,
        int followers,
        int chat,
        int events,
        string category) =>
        JsonSerializer.Serialize(new
        {
            StartedAt = startedAt,
            DurationSeconds = 3600,
            PeakViewers = peak,
            AverageViewers = average,
            FollowersGained = followers,
            ChatMessages = chat,
            AlertsPlayed = events,
            Category = category,
            Title = "Titel"
        });
}
