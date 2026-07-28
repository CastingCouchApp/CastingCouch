using System.Windows;
using System.Windows.Controls;

namespace CreatorControlSuite.App.Views.Pages.Services;

public partial class ServicesPageView : UserControl
{
    public ServicesPageView()
    {
        InitializeComponent();
        ServicesOverviewSpotifyButton.Click += (_, _) =>
            ServiceRequested?.Invoke(0);
        ServicesOverviewTwitchButton.Click += (_, _) =>
            ServiceRequested?.Invoke(1);
        ServicesOverviewObsButton.Click += (_, _) =>
            ServiceRequested?.Invoke(2);
        ServicesOverviewStreamerBotButton.Click += (_, _) =>
            ServiceRequested?.Invoke(3);
        ServicesOverviewStreamDeckButton.Click += (_, _) =>
            ServiceRequested?.Invoke(4);
    }

    public Action<int>? ServiceRequested { get; set; }

    public void ShowOverview()
    {
        ServicesOverviewPanel.Visibility = Visibility.Visible;
        ServicesTabControl.Visibility = Visibility.Collapsed;
    }

    public void SelectService(int tabIndex)
    {
        ServicesOverviewPanel.Visibility = Visibility.Collapsed;
        ServicesTabControl.Visibility = Visibility.Visible;
        if (tabIndex >= 0 &&
            tabIndex < ServicesTabControl.Items.Count)
        {
            ServicesTabControl.SelectedIndex = tabIndex;
        }
    }
}
