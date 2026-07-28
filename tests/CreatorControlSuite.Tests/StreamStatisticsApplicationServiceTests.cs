using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.Tests;

public sealed class StreamStatisticsApplicationServiceTests
{
    [Fact]
    public void CreateSnapshot_ProjectsValidRowsAndIgnoresMalformedLines()
    {
        string[] lines =
        [
            """
            {"StartedAt":"2026-07-20T18:00:00+00:00","DurationSeconds":3600,"AverageViewers":10,"PeakViewers":15,"FollowersGained":3,"NewSubscriptions":2,"GiftSubscriptions":1,"BitsCheered":100,"Category":"Music","Title":"First"}
            """,
            "not-json",
            """
            {"StartedAt":"2026-07-21T18:00:00+00:00","DurationSeconds":1800,"AverageViewers":20,"PeakViewers":25,"FollowersGained":2,"Category":"Music","Title":"Second"}
            """
        ];

        StreamStatisticsSnapshot snapshot =
            StreamStatisticsApplicationService.CreateSnapshot(lines);

        Assert.Equal("2", snapshot.TotalStreams);
        Assert.Equal("01:30", snapshot.TotalDuration);
        Assert.Equal(13.3, double.Parse(snapshot.AverageViewers));
        Assert.Equal("25", snapshot.PeakViewers);
        Assert.Equal("5", snapshot.Followers);
        Assert.Equal("00:45", snapshot.AverageDuration);
        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal("Second", snapshot.Rows[0].Title);
        Assert.Single(snapshot.Categories);
        Assert.Contains("Music · 2 Stream(s)", snapshot.Categories[0]);
        Assert.Equal(2, snapshot.Development.Count);
    }

    [Fact]
    public void CreateSnapshot_ProvidesEmptyStateMessages()
    {
        StreamStatisticsSnapshot snapshot =
            StreamStatisticsApplicationService.CreateSnapshot([]);

        Assert.Equal("0", snapshot.TotalStreams);
        Assert.Equal("00:00", snapshot.TotalDuration);
        Assert.Equal(
            "Noch keine Kategorien gespeichert.",
            Assert.Single(snapshot.Categories));
        Assert.Equal(
            "Noch keine Verlaufsdaten vorhanden.",
            Assert.Single(snapshot.Development));
    }
}
