using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Tests;

public sealed class SpotifyAutomationPageViewModelTests
{
    [Fact]
    public void Load_MapsSettingsAndSelectsConfiguredPlaylist()
    {
        var viewModel = new SpotifyAutomationPageViewModel();
        var workflow = new WorkflowSettings
        {
            AutoStartSpotifyPlaylist = true,
            AutoPlayEndMusic = true,
            PauseSpotifyOnStreamEnd = true
        };
        var spotify = new SpotifySettings
        {
            StartPlaylistUri = "spotify:playlist:two",
            ShuffleSelectedPlaylist = true,
            SetVolumeOnLiveTransition = true,
            LiveVolumePercent = 68,
            MuteDuringAlerts = true,
            FadeDuringAlerts = false,
            AlertMuteVolumePercent = 42,
            AlertFadeOutMilliseconds = 700,
            AlertFadeInMilliseconds = 900
        };
        SpotifyPlaylist[] playlists =
        [
            Playlist("one"),
            Playlist("two")
        ];

        viewModel.Load(workflow, spotify, playlists);

        Assert.True(viewModel.AutoStartOnStream);
        Assert.True(viewModel.ShuffleStartPlaylist);
        Assert.True(viewModel.PlayEndMusic);
        Assert.True(viewModel.PauseOnStreamEnd);
        Assert.True(viewModel.SetLiveVolume);
        Assert.Equal("68", viewModel.LiveVolume);
        Assert.True(viewModel.MuteDuringAlerts);
        Assert.False(viewModel.FadeDuringAlerts);
        Assert.Equal("42", viewModel.AlertVolume);
        Assert.Equal("700", viewModel.AlertFadeOutMilliseconds);
        Assert.Equal("900", viewModel.AlertFadeInMilliseconds);
        Assert.Equal("spotify:playlist:two", viewModel.SelectedPlaylist?.Uri);
    }

    [Fact]
    public void ApplyTo_ClampsValuesAndMapsDuckingMode()
    {
        var viewModel = new SpotifyAutomationPageViewModel
        {
            AutoStartOnStream = true,
            ShuffleStartPlaylist = true,
            PlayEndMusic = false,
            PauseOnStreamEnd = true,
            SetLiveVolume = true,
            LiveVolume = "120",
            MuteDuringAlerts = true,
            FadeDuringAlerts = false,
            AlertVolume = "-5",
            AlertFadeOutMilliseconds = "12000",
            AlertFadeInMilliseconds = "invalid"
        };
        viewModel.UpdatePlaylists([Playlist("selected")]);
        viewModel.SelectedPlaylist = viewModel.Playlists[0];
        var workflow = new WorkflowSettings();
        var spotify = new SpotifySettings();

        viewModel.ApplyTo(workflow, spotify);

        Assert.True(workflow.AutoStartSpotifyPlaylist);
        Assert.True(spotify.ShuffleSelectedPlaylist);
        Assert.False(workflow.AutoPlayEndMusic);
        Assert.True(workflow.PauseSpotifyOnStreamEnd);
        Assert.True(spotify.SetVolumeOnLiveTransition);
        Assert.Equal(100, spotify.LiveVolumePercent);
        Assert.True(spotify.MuteDuringAlerts);
        Assert.False(spotify.FadeDuringAlerts);
        Assert.Equal("Reduce", spotify.AlertDuckingMode);
        Assert.Equal(0, spotify.AlertMuteVolumePercent);
        Assert.Equal(10000, spotify.AlertFadeOutMilliseconds);
        Assert.Equal(500, spotify.AlertFadeInMilliseconds);
        Assert.Equal("spotify:playlist:selected", spotify.StartPlaylistUri);
    }

    [Fact]
    public void UpdatePlaylists_PreservesSelectionByUri()
    {
        var viewModel = new SpotifyAutomationPageViewModel();
        var workflow = new WorkflowSettings();
        var spotify = new SpotifySettings
        {
            StartPlaylistUri = "spotify:playlist:two"
        };
        viewModel.Load(workflow, spotify, [Playlist("one"), Playlist("two")]);

        viewModel.UpdatePlaylists([Playlist("two"), Playlist("three")]);

        Assert.Equal("spotify:playlist:two", viewModel.SelectedPlaylist?.Uri);
    }

    [Fact]
    public async Task SaveCommand_DelegatesToApplicationWorkflow()
    {
        var viewModel = new SpotifyAutomationPageViewModel();
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

    private static SpotifyPlaylist Playlist(string id) =>
        new(
            id,
            $"spotify:playlist:{id}",
            $"Playlist {id}",
            "Owner",
            "",
            10);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 20 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
