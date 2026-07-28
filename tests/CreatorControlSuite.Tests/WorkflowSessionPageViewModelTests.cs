using System.Globalization;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.Tests;

public sealed class WorkflowSessionPageViewModelTests
{
    [Fact]
    public void Update_MapsWorkflowStateAndSessionStatistics()
    {
        var viewModel = new WorkflowSessionPageViewModel();
        var state = new WorkflowState(
            StreamPhase.Countdown,
            DateTimeOffset.UtcNow,
            null,
            null,
            125,
            "Starting Soon",
            "Bereit");
        var stats = new StreamSessionStats
        {
            FollowersAtStart = 100,
            FollowersAtEnd = 107,
            ChatMessages = 42,
            AlertsPlayed = 3
        };
        stats.ViewerSamples.Add(
            new ViewerSample(DateTimeOffset.UtcNow, 10));
        stats.ViewerSamples.Add(
            new ViewerSample(DateTimeOffset.UtcNow, 20));

        viewModel.Update(state, stats);

        Assert.Equal("Countdown", viewModel.Phase);
        Assert.Equal("02:05", viewModel.Countdown);
        Assert.Equal("Starting Soon", viewModel.Scene);
        Assert.Equal("20", viewModel.PeakViewers);
        Assert.Equal(
            15d.ToString("0.0", CultureInfo.CurrentCulture),
            viewModel.AverageViewers);
        Assert.Equal("7", viewModel.Followers);
        Assert.Equal("42 / 3", viewModel.ChatAlerts);
    }

    [Fact]
    public void Update_UsesPlaceholderForMissingScene()
    {
        var viewModel = new WorkflowSessionPageViewModel();
        var state = new WorkflowState(
            StreamPhase.Idle,
            null,
            null,
            null,
            0,
            "",
            "Bereit");

        viewModel.Update(state, new StreamSessionStats());

        Assert.Equal("-", viewModel.Scene);
        Assert.Equal("00:00", viewModel.Countdown);
    }

    [Fact]
    public async Task AddViewerSampleCommand_ValidatesAndDelegates()
    {
        var viewModel = new WorkflowSessionPageViewModel
        {
            ViewerSample = "invalid"
        };
        int? received = null;
        viewModel.AddViewerSampleRequestedAsync = viewers =>
        {
            received = viewers;
            return Task.CompletedTask;
        };

        viewModel.AddViewerSampleCommand.Execute(null);
        await Task.Delay(25);
        Assert.Null(received);

        viewModel.ViewerSample = "23";
        viewModel.AddViewerSampleCommand.Execute(null);
        await WaitUntilAsync(() => received.HasValue);

        Assert.Equal(23, received);
    }

    [Fact]
    public async Task ResetCommand_DelegatesToApplicationWorkflow()
    {
        var viewModel = new WorkflowSessionPageViewModel();
        bool reset = false;
        viewModel.ResetRequestedAsync = () =>
        {
            reset = true;
            return Task.CompletedTask;
        };

        viewModel.ResetCommand.Execute(null);
        await WaitUntilAsync(() => reset);

        Assert.True(reset);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 20 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
