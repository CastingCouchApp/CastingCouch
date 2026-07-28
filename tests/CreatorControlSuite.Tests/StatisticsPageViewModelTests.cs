using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.ViewModels.Pages;

namespace CreatorControlSuite.Tests;

public sealed class StatisticsPageViewModelTests
{
    [Fact]
    public async Task LoadAsync_MapsSnapshot()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(
            path,
            """{"DurationSeconds":60,"AverageViewers":5,"PeakViewers":7,"FollowersGained":1,"Category":"Talk"}""");
        var viewModel = new StatisticsPageViewModel(
            new StreamStatisticsApplicationService());

        await viewModel.LoadAsync(path);

        Assert.Equal("1", viewModel.TotalStreams);
        Assert.Equal("00:01", viewModel.TotalDuration);
        Assert.Equal(5.0, double.Parse(viewModel.AverageViewers));
        Assert.Single(viewModel.Rows);
    }

    [Fact]
    public async Task SelectMetric_DelegatesChangedValue()
    {
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new StatisticsPageViewModel(
            new StreamStatisticsApplicationService())
        {
            MetricChangedAsync = metric =>
            {
                completion.SetResult(metric);
                return Task.CompletedTask;
            }
        };

        viewModel.LoadMetric("ViewerCount");
        viewModel.SelectedMetric = "FollowerCount";

        Assert.Equal(
            "FollowerCount",
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
