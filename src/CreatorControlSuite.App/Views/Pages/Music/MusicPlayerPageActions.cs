namespace CreatorControlSuite.App.Views.Pages.Music;

public sealed record MusicPlayerPageActions(
    Func<Task> PreviousAsync,
    Func<Task> PlayPauseAsync,
    Func<Task> NextAsync,
    Func<Task> ConnectAsync,
    Func<Task> DisconnectAsync,
    Func<Task<string>> CopyBookmarkletAsync,
    Func<Task> OpenBookmarkletInstallAsync,
    Func<Task<MusicBookmarkletDragData>> GetBookmarkletDragDataAsync,
    Func<Task> OpenSpotifyServiceAsync,
    Func<double, Task> SeekAsync,
    Func<int, Task> SetVolumeAsync);

public sealed record MusicBookmarkletDragData(
    string Bookmarklet,
    string Title);
