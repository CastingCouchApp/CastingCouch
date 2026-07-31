using System.Windows;
using System.Windows.Controls;

namespace CreatorControlSuite.App.Views.Pages.Services;

public partial class TwitchServiceView : UserControl
{
    private Window? _statisticsWindow;
    private Window? _intelligenceWindow;

    public TwitchServiceView()
    {
        InitializeComponent();
        ServicesOpenTwitchStatisticsButton.Click += (_, _) =>
            OpenPopout(
                ServicesTwitchStatisticsContentHost,
                "Twitch · Stream-Statistiken",
                ServicesOpenTwitchStatisticsButton,
                _statisticsWindow,
                value => _statisticsWindow = value);
        ServicesOpenTwitchIntelligenceButton.Click += (_, _) =>
            OpenPopout(
                ServicesTwitchIntelligenceContentHost,
                "Twitch · Intelligence-Analyse (TEST)",
                ServicesOpenTwitchIntelligenceButton,
                _intelligenceWindow,
                value => _intelligenceWindow = value);
    }

    private void OpenPopout(
        ContentControl host,
        string title,
        Button sourceButton,
        Window? trackedWindow,
        Action<Window?> setTrackedWindow)
    {
        if (trackedWindow is not null)
        {
            trackedWindow.Activate();
            return;
        }

        object? content = host.Content;
        if (content is null)
        {
            return;
        }

        host.Content = null;
        sourceButton.IsEnabled = false;
        var window = new Window
        {
            Title = title,
            Owner = Window.GetWindow(this),
            Width = 1100,
            Height = 760,
            MinWidth = 760,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Padding = new Thickness(22),
                    Child = content as UIElement
                }
            }
        };

        setTrackedWindow(window);
        window.Closed += (_, _) =>
        {
            if (window.Content is ScrollViewer scrollViewer &&
                scrollViewer.Content is Border border)
            {
                border.Child = null;
            }

            host.Content = content;
            sourceButton.IsEnabled = true;
            setTrackedWindow(null);
        };
        window.Show();
    }
}
