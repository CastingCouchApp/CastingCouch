using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class TwitchGoalsPageViewModelTests
{
    [Fact]
    public void Load_MapsSettingsAndLiveCounts()
    {
        var viewModel = new TwitchGoalsPageViewModel();
        var obs = new ObsSettings
        {
            GoalOverlayScene = "Goals"
        };
        var twitch = new TwitchSettings();
        twitch.FollowerGoal.Target = 500;
        twitch.SubGoal.Target = 40;

        viewModel.Load(obs, twitch, 123, 17);

        Assert.Equal("Goals", viewModel.OverlayScene);
        Assert.Equal("123", viewModel.FollowerCurrent);
        Assert.Equal("500", viewModel.FollowerTarget);
        Assert.Equal("17", viewModel.SubscriptionCurrent);
        Assert.Equal("40", viewModel.SubscriptionTarget);
    }

    [Fact]
    public void ApplyTo_UsesLiveCountsAndNormalizesFields()
    {
        var viewModel = new TwitchGoalsPageViewModel
        {
            OverlayScene = " ",
            FollowerTitle = " ",
            FollowerCurrent = "10",
            FollowerTarget = "250,5",
            FollowerFontFace = " Inter ",
            FollowerFontSize = "42",
            SubscriptionCurrent = "4",
            DonationTitle = " Support ",
            DonationCurrent = "12,5",
            DonationTarget = "1000",
            DonationCurrency = " EUR "
        };
        viewModel.UpdateLiveCounts(125, 8);
        var obs = new ObsSettings();
        var twitch = new TwitchSettings();

        viewModel.ApplyTo(obs, twitch);

        Assert.Equal("CCS Ziele & Overlay-Daten", obs.GoalOverlayScene);
        Assert.Equal("Follower-Ziel", twitch.FollowerGoal.Title);
        Assert.Equal(125, twitch.FollowerGoal.Current);
        Assert.Equal(250.5, twitch.FollowerGoal.Target);
        Assert.Equal("Inter", twitch.FollowerGoal.FontFace);
        Assert.Equal(42, twitch.FollowerGoal.FontSize);
        Assert.Equal(8, twitch.SubGoal.Current);
        Assert.Equal("Support", twitch.DonationGoal.Title);
        Assert.Equal(12.5, twitch.DonationGoal.Current);
        Assert.Equal("EUR", twitch.DonationGoal.Currency);
    }

    [Fact]
    public async Task SaveCommand_DelegatesToApplicationWorkflow()
    {
        var viewModel = new TwitchGoalsPageViewModel();
        bool saved = false;
        viewModel.SaveRequestedAsync = () =>
        {
            saved = true;
            return Task.CompletedTask;
        };

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => saved);

        Assert.True(saved);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 20 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
