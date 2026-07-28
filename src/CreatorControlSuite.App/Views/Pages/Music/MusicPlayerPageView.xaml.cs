using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.App.Views.Pages.Music;

public partial class MusicPlayerPageView : UserControl
{
    private Point _bookmarkletDragStart;
    private bool _bookmarkletDragPending;
    private bool _applyingState;

    public MusicPlayerPageView()
    {
        InitializeComponent();
        MusicPlayerPreviousButton.Click += async (_, _) => await InvokeAsync(actions => actions.PreviousAsync());
        MusicPlayerPlayPauseButton.Click += async (_, _) => await InvokeAsync(actions => actions.PlayPauseAsync());
        MusicPlayerNextButton.Click += async (_, _) => await InvokeAsync(actions => actions.NextAsync());
        MusicPlayerConnectButton.Click += async (_, _) => await InvokeAsync(actions => actions.ConnectAsync());
        MusicPlayerDisconnectButton.Click += async (_, _) => await InvokeAsync(actions => actions.DisconnectAsync());
        MusicPlayerCopyBookmarkletButton.Click += CopyBookmarkletButton_Click;
        MusicPlayerOpenBookmarkletInstallButton.Click += OpenBookmarkletInstallButton_Click;
        MusicPlayerOpenSpotifyServiceButton.Click += async (_, _) => await InvokeAsync(actions => actions.OpenSpotifyServiceAsync());
        MusicPlayerProgressBar.PreviewMouseLeftButtonUp += MusicPlayerProgressBar_PreviewMouseLeftButtonUp;
        MusicPlayerVolumeSlider.ValueChanged += MusicPlayerVolumeSlider_ValueChanged;
        MusicPlayerBookmarkletDragChip.PreviewMouseLeftButtonDown += BookmarkletDragChip_PreviewMouseLeftButtonDown;
        MusicPlayerBookmarkletDragChip.PreviewMouseMove += BookmarkletDragChip_PreviewMouseMove;
    }

    public MusicPlayerPageActions? Actions { get; set; }

    public void ApplyProvider(
        string displayName,
        bool isSpotify)
    {
        MusicPlayerSubtitleText.Text =
            "Aktiver Provider: " + displayName;
        MusicPlayerYouTubePanel.Visibility = isSpotify
            ? Visibility.Collapsed
            : Visibility.Visible;
        MusicPlayerSpotifyHintPanel.Visibility = isSpotify
            ? Visibility.Visible
            : Visibility.Collapsed;
        MusicPlayerVolumePanel.Visibility = isSpotify
            ? Visibility.Visible
            : Visibility.Collapsed;
        MusicPlayerProgressBar.IsEnabled = isSpotify;
        MusicPlayerAlbumText.Visibility = isSpotify
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void ApplyState(
        MusicPlayerUiState state,
        bool showAlbum)
    {
        _applyingState = true;
        try
        {
            MusicNowPlayingWidget.SetState(state);
            MusicPlayerAlbumText.Visibility = showAlbum
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (showAlbum)
            {
                MusicPlayerAlbumText.Text =
                    string.IsNullOrWhiteSpace(state.Album)
                        ? "Album: -"
                        : "Album: " + state.Album;
            }

            MusicPlayerTitleText.Text =
                string.IsNullOrWhiteSpace(state.Title)
                    ? "Kein Titel"
                    : state.Title;
            MusicPlayerArtistText.Text =
                string.IsNullOrWhiteSpace(state.Artist)
                    ? "-"
                    : state.Artist;
            MusicPlayerStatusText.Text = state.StatusText;
            MusicPlayerPlayPauseButton.Content =
                state.IsPlaying ? "Pause" : "Play";

            int duration = Math.Max(1, state.DurationMs);
            int progress = Math.Clamp(
                state.PositionMs,
                0,
                duration);
            MusicPlayerProgressBar.Value = state.DurationMs <= 0
                ? 0
                : (double)progress / duration;
            MusicPlayerProgressText.Text =
                TimeSpan.FromMilliseconds(progress).ToString(@"mm\:ss");
            MusicPlayerDurationText.Text =
                TimeSpan.FromMilliseconds(Math.Max(0, state.DurationMs))
                    .ToString(@"mm\:ss");
            if (state.VolumePercent is int volume)
            {
                MusicPlayerVolumeSlider.Value = volume;
                MusicPlayerVolumeText.Text = $"{volume} %";
            }
        }
        finally
        {
            _applyingState = false;
        }
    }

    public void SetCover(ImageSource? image)
        => MusicPlayerCoverImage.Source = image;

    public void SetBookmarklet(
        string bookmarklet,
        string title,
        string status,
        bool showText)
    {
        MusicPlayerBookmarkletBox.Text = bookmarklet;
        MusicPlayerBookmarkletBox.Visibility = showText
            ? Visibility.Visible
            : Visibility.Collapsed;
        MusicPlayerBookmarkletDragLabel.Text = title;
        MusicPlayerBridgeStatusText.Text = status;
    }

    public void SetBridgeStatus(string status)
        => MusicPlayerBridgeStatusText.Text = status;

    public void UpdateBookmarklet(
        string bookmarklet,
        string title,
        string status)
    {
        MusicPlayerBookmarkletBox.Text = bookmarklet;
        MusicPlayerBookmarkletDragLabel.Text = title;
        MusicPlayerBridgeStatusText.Text = status;
    }

    private async void CopyBookmarkletButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            string bookmarklet = await RequireActions().CopyBookmarkletAsync();
            Clipboard.SetText(bookmarklet);
            SetBookmarklet(
                bookmarklet,
                MusicPlayerBookmarkletDragLabel.Text,
                "Bookmarklet in die Zwischenablage kopiert.",
                showText: true);
        }
        catch (Exception exception)
        {
            ShowWarning(exception.Message, "Bookmarklet");
        }
    }

    private async void OpenBookmarkletInstallButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await RequireActions().OpenBookmarkletInstallAsync();
            SetBridgeStatus("Install-Seite geöffnet – Link in die Lesezeichenleiste ziehen.");
        }
        catch (Exception exception)
        {
            ShowWarning(exception.Message, "YouTube Music");
        }
    }

    private async void MusicPlayerProgressBar_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
        => await InvokeAsync(actions => actions.SeekAsync(MusicPlayerProgressBar.Value));

    private async void MusicPlayerVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        int volume = (int)Math.Round(e.NewValue);
        MusicPlayerVolumeText.Text = $"{volume} %";
        if (!_applyingState)
        {
            await InvokeAsync(actions => actions.SetVolumeAsync(volume));
        }
    }

    private void BookmarkletDragChip_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _bookmarkletDragStart = e.GetPosition(null);
        _bookmarkletDragPending = true;
    }

    private async void BookmarkletDragChip_PreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_bookmarkletDragPending ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point position = e.GetPosition(null);
        if (Math.Abs(position.X - _bookmarkletDragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _bookmarkletDragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _bookmarkletDragPending = false;
        try
        {
            MusicBookmarkletDragData data =
                await RequireActions().GetBookmarkletDragDataAsync();
            MusicPlayerBookmarkletDragLabel.Text = data.Title;
            StartBookmarkletDrag(data);
        }
        catch (Exception exception)
        {
            ShowWarning(
                "Bookmarklet-Drag nicht möglich: " + exception.Message +
                "\n\nBitte zuerst verbinden und ggf. „Install-Seite öffnen“ nutzen.",
                "YouTube Music");
        }
    }

    private void StartBookmarkletDrag(
        MusicBookmarkletDragData bookmarklet)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, bookmarklet.Bookmarklet);
        data.SetData(DataFormats.Text, bookmarklet.Bookmarklet);
        data.SetData(
            "text/uri-list",
            bookmarklet.Bookmarklet + "\r\n");
        data.SetData(
            "text/x-moz-url",
            bookmarklet.Bookmarklet + "\n" + bookmarklet.Title);
        data.SetData(
            DataFormats.Html,
            BuildBookmarkletHtml(
                bookmarklet.Bookmarklet,
                bookmarklet.Title));
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
    }

    private static string BuildBookmarkletHtml(
        string bookmarklet,
        string title)
    {
        string href = System.Net.WebUtility.HtmlEncode(bookmarklet);
        string label = System.Net.WebUtility.HtmlEncode(title);
        string fragment = $"<a href=\"{href}\">{label}</a>";
        const string prefix =
            "Version:0.9\r\nStartHTML:{0:D8}\r\nEndHTML:{1:D8}\r\nStartFragment:{2:D8}\r\nEndFragment:{3:D8}\r\n";
        int headerLength = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            prefix,
            0,
            0,
            0,
            0).Length;
        int end = headerLength + Encoding.UTF8.GetByteCount(fragment);
        return string.Format(
                   System.Globalization.CultureInfo.InvariantCulture,
                   prefix,
                   headerLength,
                   end,
                   headerLength,
                   end)
               + fragment;
    }

    private async Task InvokeAsync(
        Func<MusicPlayerPageActions, Task> action)
    {
        try
        {
            await action(RequireActions());
        }
        catch (Exception exception)
        {
            ShowWarning(exception.Message, "Music Player");
        }
    }

    private MusicPlayerPageActions RequireActions()
        => Actions ??
           throw new InvalidOperationException(
               "Music-Player-Aktionen sind nicht konfiguriert.");

    private static void ShowWarning(
        string message,
        string title)
        => MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
