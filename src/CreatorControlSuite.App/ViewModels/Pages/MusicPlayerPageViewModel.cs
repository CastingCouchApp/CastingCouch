using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.App.ViewModels.Pages;

/// <summary>Thin remote control for the music player page.</summary>
public sealed class MusicPlayerPageViewModel : ViewModelBase, IPageViewModel
{
    private readonly IMusicPlayerRouter _router;

    public MusicPlayerPageViewModel(IMusicPlayerRouter router)
    {
        _router = router;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ConnectCommand = new AsyncRelayCommand(_ => ConnectAsync());
        DisconnectCommand = new AsyncRelayCommand(_ => DisconnectAsync());
        PlayPauseCommand = new AsyncRelayCommand(_ => _router.PlayPauseAsync());
        NextCommand = new AsyncRelayCommand(_ => _router.NextAsync());
        PreviousCommand = new AsyncRelayCommand(_ => _router.PreviousAsync());
    }

    public string Key => "music";
    public string Title => "Music Player";

    public string Subtitle
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Aktiver Provider: –";

    public string StatusMessage
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit.";

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand PlayPauseCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }

    public Task OnNavigatedToAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Subtitle = "Aktiver Provider: " + (_router.ActiveDisplayName.Length > 0 ? _router.ActiveDisplayName : "–");
        StatusMessage = string.IsNullOrWhiteSpace(_router.ActiveProviderId) ? "Kein Player aktiv." : "Player bereit.";
        return Task.CompletedTask;
    }

    private async Task ConnectAsync()
    {
        await _router.ConnectActiveAsync();
        StatusMessage = "Verbindung angefordert.";
        await RefreshAsync();
    }

    private async Task DisconnectAsync()
    {
        await _router.DisconnectActiveAsync();
        StatusMessage = "Getrennt.";
        await RefreshAsync();
    }
}
