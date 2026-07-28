using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class SpotifyAutomationPageViewModel : ViewModelBase
{
    private string _configuredStartPlaylistUri = "";

    public SpotifyAutomationPageViewModel()
    {
        SaveCommand = new AsyncRelayCommand(
            _ => SaveRequestedAsync?.Invoke() ?? Task.CompletedTask);
    }

    public IReadOnlyList<SpotifyPlaylist> Playlists
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public SpotifyPlaylist? SelectedPlaylist
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool AutoStartOnStream
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool PlayEndMusic
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool PauseOnStreamEnd
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool SetLiveVolume
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string LiveVolume
    {
        get;
        set => SetProperty(ref field, value);
    } = "75";

    public bool MuteDuringAlerts
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string AlertVolume
    {
        get;
        set => SetProperty(ref field, value);
    } = "50";

    public string AlertFadeOutMilliseconds
    {
        get;
        set => SetProperty(ref field, value);
    } = "500";

    public string AlertFadeInMilliseconds
    {
        get;
        set => SetProperty(ref field, value);
    } = "500";

    public string AutomationStatusText
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string AlertStatusText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit";

    public string AlertStatusState
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Normal";

    public AsyncRelayCommand SaveCommand { get; }

    public Func<Task>? SaveRequestedAsync { get; set; }

    public void Load(
        WorkflowSettings workflow,
        SpotifySettings spotify,
        IReadOnlyList<SpotifyPlaylist> playlists)
    {
        _configuredStartPlaylistUri = spotify.StartPlaylistUri;
        AutoStartOnStream = workflow.AutoStartSpotifyPlaylist;
        PlayEndMusic = workflow.AutoPlayEndMusic;
        PauseOnStreamEnd = workflow.PauseSpotifyOnStreamEnd;
        SetLiveVolume = spotify.SetVolumeOnLiveTransition;
        LiveVolume = spotify.LiveVolumePercent.ToString();
        MuteDuringAlerts = spotify.MuteDuringAlerts;
        AlertVolume = spotify.AlertMuteVolumePercent.ToString();
        AlertFadeOutMilliseconds =
            spotify.AlertFadeOutMilliseconds.ToString();
        AlertFadeInMilliseconds =
            spotify.AlertFadeInMilliseconds.ToString();
        UpdatePlaylists(playlists);
    }

    public void UpdatePlaylists(IReadOnlyList<SpotifyPlaylist> playlists)
    {
        string selectedUri =
            SelectedPlaylist?.Uri ?? _configuredStartPlaylistUri;
        Playlists = playlists;
        SelectedPlaylist = playlists.FirstOrDefault(
            playlist => string.Equals(
                playlist.Uri,
                selectedUri,
                StringComparison.Ordinal));
    }

    public void ApplyTo(
        WorkflowSettings workflow,
        SpotifySettings spotify)
    {
        workflow.AutoStartSpotifyPlaylist = AutoStartOnStream;
        workflow.AutoPlayEndMusic = PlayEndMusic;
        workflow.PauseSpotifyOnStreamEnd = PauseOnStreamEnd;
        spotify.SetVolumeOnLiveTransition = SetLiveVolume;
        spotify.LiveVolumePercent = ParseClamped(
            LiveVolume,
            fallback: 75,
            maximum: 100);
        spotify.MuteDuringAlerts = MuteDuringAlerts;
        spotify.AlertDuckingMode = MuteDuringAlerts ? "Reduce" : "None";
        spotify.AlertMuteVolumePercent = ParseClamped(
            AlertVolume,
            fallback: 50,
            maximum: 100);
        spotify.AlertFadeOutMilliseconds = ParseClamped(
            AlertFadeOutMilliseconds,
            fallback: 500,
            maximum: 10_000);
        spotify.AlertFadeInMilliseconds = ParseClamped(
            AlertFadeInMilliseconds,
            fallback: 500,
            maximum: 10_000);

        if (SelectedPlaylist is not null)
        {
            spotify.StartPlaylistUri = SelectedPlaylist.Uri;
            _configuredStartPlaylistUri = SelectedPlaylist.Uri;
        }
    }

    public void SetAlertStatus(
        string text,
        string state = "Normal")
    {
        AlertStatusText = text;
        AlertStatusState = state;
    }

    private static int ParseClamped(
        string text,
        int fallback,
        int maximum) =>
        int.TryParse(text, out int value)
            ? Math.Clamp(value, 0, maximum)
            : fallback;
}
