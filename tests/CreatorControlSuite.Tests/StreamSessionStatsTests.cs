using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.Tests;

public sealed class StreamSessionStatsTests
{
    [Fact]
    public void CalculatesPeakAndAverage()
    {
        var stats = new StreamSessionStats();
        stats.ViewerSamples.Add(new ViewerSample(DateTimeOffset.Now, 10));
        stats.ViewerSamples.Add(new ViewerSample(DateTimeOffset.Now, 20));
        stats.ViewerSamples.Add(new ViewerSample(DateTimeOffset.Now, 30));

        Assert.Equal(30, stats.PeakViewers);
        Assert.Equal(20, stats.AverageViewers);
    }

    [Fact]
    public void FollowersGainedNeverReturnsNegative()
    {
        var stats = new StreamSessionStats
        {
            FollowersAtStart = 100,
            FollowersAtEnd = 95
        };

        Assert.Equal(0, stats.FollowersGained);
    }
}
