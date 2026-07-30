using System.Collections.ObjectModel;
using System.Windows;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.App.Views.Dialogs;

public partial class SpotifySceneMusicWindow : Window
{
    public ObservableCollection<SpotifySceneMusicRow> Rows { get; }

    public SpotifySceneMusicWindow(
        IEnumerable<SpotifySceneMusicRow> rows,
        IEnumerable<SpotifyPlaylist> playlists)
    {
        InitializeComponent();
        Rows = new ObservableCollection<SpotifySceneMusicRow>(rows);
        RulesGrid.ItemsSource = Rows;
        PlaylistColumn.ItemsSource = playlists.ToList();
        CancelButton.Click += (_, _) => DialogResult = false;
        SaveButton.Click += (_, _) =>
        {
            RulesGrid.CommitEdit();
            RulesGrid.CommitEdit();
            DialogResult = true;
        };
    }
}
