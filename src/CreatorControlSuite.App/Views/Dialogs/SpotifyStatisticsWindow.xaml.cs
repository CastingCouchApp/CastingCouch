using System.Windows;
using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.App.Views.Dialogs;

public partial class SpotifyStatisticsWindow : Window
{
    private readonly SpotifyListeningStatisticsService _statistics;

    public SpotifyStatisticsWindow(SpotifyListeningStatisticsService statistics)
    {
        InitializeComponent();
        _statistics = statistics;
        Refresh();
        CloseButton.Click += (_, _) => Close();
        ResetButton.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    "Spotify-Statistik wirklich zurücksetzen?",
                    "Spotify-Statistik",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _statistics.Reset();
            Refresh();
        };
    }

    private void Refresh()
    {
        SpotifyListeningStatisticsSnapshot snapshot = _statistics.GetSnapshot();
        SummaryText.Text =
            $"{snapshot.TotalPlays} erkannte Titelstarts · {snapshot.TotalListeningTime:hh\\:mm\\:ss} Wiedergabezeit";
        TracksList.ItemsSource = snapshot.TopTracks;
        ArtistsList.ItemsSource = snapshot.TopArtists;
    }
}
