using System.Windows;
using System.Windows.Controls;
using CreatorControlSuite.App.ViewModels.Pages;

namespace CreatorControlSuite.App.Views.Pages.Spotify;

public partial class SpotifyAutomationView : UserControl
{
    public SpotifyAutomationView()
    {
        InitializeComponent();
    }

    private void AutoSave_OnChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is SpotifyAutomationPageViewModel viewModel &&
            viewModel.SaveCommand.CanExecute(null))
        {
            viewModel.SaveCommand.Execute(null);
        }
    }
}
