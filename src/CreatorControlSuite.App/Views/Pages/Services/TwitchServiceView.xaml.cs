using System.Windows;
using System.Windows.Controls;

namespace CreatorControlSuite.App.Views.Pages.Services;

public partial class TwitchServiceView : UserControl
{
    private Window? _statisticsWindow;
    private Window? _intelligenceWindow;
    private Window? _pollWindow;
    private Window? _predictionWindow;
    private Window? _channelPointsWindow;
    private bool _updatingChannelEditor;

    public bool IsChannelEditorDirty { get; private set; }

    public TwitchServiceView()
    {
        InitializeComponent();
        ServicesCompactOpenTwitchPollButton.Click += (_, _) =>
            ServicesOpenTwitchPollButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ServicesCompactOpenTwitchPredictionButton.Click += (_, _) =>
            ServicesOpenTwitchPredictionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ServicesCompactOpenTwitchChannelPointsButton.Click += (_, _) =>
            ServicesOpenTwitchChannelPointsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ServicesTwitchTitleBox.TextChanged += (_, _) => MarkChannelEditorDirty();
        ServicesTwitchCategorySearchBox.TextChanged += (_, _) => MarkChannelEditorDirty();
        ServicesTwitchCategoryResultsBox.SelectionChanged += (_, _) => MarkChannelEditorDirty();
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
        ServicesOpenTwitchPollButton.Click += (_, _) =>
            OpenPopout(
                ServicesTwitchPollTab,
                "Twitch · Umfrage starten",
                ServicesOpenTwitchPollButton,
                _pollWindow,
                value => _pollWindow = value);
        ServicesOpenTwitchPredictionButton.Click += (_, _) =>
            OpenPopout(
                ServicesTwitchPredictionTab,
                "Twitch · Vorhersage starten",
                ServicesOpenTwitchPredictionButton,
                _predictionWindow,
                value => _predictionWindow = value);
        ServicesOpenTwitchChannelPointsButton.Click += (_, _) =>
            OpenPopout(
                ServicesTwitchChannelPointsTab,
                "Twitch · Kanalpunkte verwalten",
                ServicesOpenTwitchChannelPointsButton,
                _channelPointsWindow,
                value => _channelPointsWindow = value);
    }

    public void RefreshChannelEditor(string title, string category)
    {
        if (IsChannelEditorDirty ||
            ServicesTwitchTitleBox.IsKeyboardFocusWithin ||
            ServicesTwitchCategorySearchBox.IsKeyboardFocusWithin ||
            ServicesTwitchCategoryResultsBox.IsKeyboardFocusWithin)
        {
            return;
        }

        _updatingChannelEditor = true;
        try
        {
            ServicesTwitchTitleBox.Text = title;
            ServicesTwitchCategorySearchBox.Text = category;
        }
        finally
        {
            _updatingChannelEditor = false;
        }
    }

    public void MarkChannelEditorSaved()
    {
        IsChannelEditorDirty = false;
    }

    private void MarkChannelEditorDirty()
    {
        if (!_updatingChannelEditor)
        {
            IsChannelEditorDirty = true;
        }
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
