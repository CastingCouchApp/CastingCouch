using System.Collections.ObjectModel;
using System.Windows;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.App.Views.Dialogs;

public partial class SpotifySceneMusicWindow : Window
{
    public ObservableCollection<SpotifySceneMusicRow> Rows { get; }
    public IReadOnlyList<SpotifyPlaylist> Playlists { get; }

    public SpotifySceneMusicWindow(
        IEnumerable<SpotifySceneMusicRow> rows,
        IEnumerable<SpotifyPlaylist> playlists)
    {
        InitializeComponent();
        Rows = new ObservableCollection<SpotifySceneMusicRow>(rows);
        Playlists = playlists.ToList();
        DataContext = this;
        RulesGrid.ItemsSource = Rows;
        CancelButton.Click += (_, _) => DialogResult = false;
        SaveButton.Click += (_, _) =>
        {
            RulesGrid.CommitEdit();
            RulesGrid.CommitEdit();
            SpotifySceneMusicRow? invalid = Rows.FirstOrDefault(
                row => row.Enabled && string.IsNullOrWhiteSpace(row.PlaylistUri));
            if (invalid is not null)
            {
                MessageBox.Show(
                    this,
                    $"Bitte für die Szene „{invalid.SceneName}“ eine Playlist auswählen.",
                    "Spotify-Szenenmusik",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        };
    }
}
