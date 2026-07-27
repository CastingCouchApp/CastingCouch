using System.Windows;
using System.Windows.Controls;
using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.App.Controls;

public partial class NowPlayingWidget : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(NowPlayingWidget),
            new PropertyMetadata("Kein Titel", OnTitleChanged));

    public static readonly DependencyProperty ArtistProperty =
        DependencyProperty.Register(
            nameof(Artist),
            typeof(string),
            typeof(NowPlayingWidget),
            new PropertyMetadata("-", OnArtistChanged));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(
            nameof(IsPlaying),
            typeof(bool),
            typeof(NowPlayingWidget),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public NowPlayingWidget()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Artist
    {
        get => (string)GetValue(ArtistProperty);
        set => SetValue(ArtistProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public void SetState(MusicPlayerUiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Title = string.IsNullOrWhiteSpace(state.Title) ? "Kein Titel" : state.Title;
        Artist = string.IsNullOrWhiteSpace(state.Artist) ? "-" : state.Artist;
        IsPlaying = state.IsPlaying;
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NowPlayingWidget widget)
        {
            widget.TitleText.Text = e.NewValue as string ?? "Kein Titel";
        }
    }

    private static void OnArtistChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NowPlayingWidget widget)
        {
            widget.ArtistText.Text = e.NewValue as string ?? "-";
        }
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NowPlayingWidget widget)
        {
            widget.PlayingGlyph.Text = (bool)e.NewValue! ? "Ⅱ" : "▶";
        }
    }
}
