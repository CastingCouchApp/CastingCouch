using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Hud;

public partial class StreamerHudWindow : Window
{
    private ObservableCollection<string>? _chatItems;
    private ObservableCollection<string>? _eventItems;

    public StreamerHudWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyNativeStyles();
        Loaded += (_, _) =>
        {
            ApplyNativeStyles();
            ScrollListsToEnd();
        };
    }

    public void BindSources(ObservableCollection<string> chatItems, ObservableCollection<string> eventItems)
    {
        _chatItems?.CollectionChanged -= OnChatCollectionChanged;

        _eventItems?.CollectionChanged -= OnEventCollectionChanged;

        _chatItems = chatItems;
        _eventItems = eventItems;
        ChatList.ItemsSource = chatItems;
        EventsList.ItemsSource = eventItems;

        _chatItems.CollectionChanged += OnChatCollectionChanged;
        _eventItems.CollectionChanged += OnEventCollectionChanged;
        ScrollListsToEnd();
    }

    public void ApplySettings(StreamerHudSettings settings)
    {
        Width = Math.Clamp(settings.PanelWidth, 280, 800);
        Opacity = Math.Clamp(settings.Opacity, 0.3, 1.0);
        LiveStatusPanel.Visibility = settings.ShowLiveStatus ? Visibility.Visible : Visibility.Collapsed;
        ChatPanel.Visibility = settings.ShowChat ? Visibility.Visible : Visibility.Collapsed;
        EventsPanel.Visibility = settings.ShowEvents ? Visibility.Visible : Visibility.Collapsed;

        PositionOnMonitor(settings);
        ApplyNativeStyles();
        NativeWindowHelper.SetClickThrough(this, settings.ClickThrough);

        // Bottom anchors need a measured height; re-apply after layout.
        Dispatcher.BeginInvoke(() =>
        {
            PositionOnMonitor(settings);
            NativeWindowHelper.ExcludeFromCapture(this);
            NativeWindowHelper.SetClickThrough(this, settings.ClickThrough);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void UpdateLiveStatus(string text)
    {
        LiveStatusText.Text = string.IsNullOrWhiteSpace(text) ? "OFFLINE" : text;
    }

    private void PositionOnMonitor(StreamerHudSettings settings)
    {
        NativeWindowHelper.MonitorInfo monitor = NativeWindowHelper.ResolveMonitor(settings.MonitorIndex);
        int margin = Math.Max(0, settings.Margin);
        UpdateLayout();
        double height = ActualHeight > 0 ? ActualHeight : 420;

        double left = settings.Anchor switch
        {
            "TopLeft" or "BottomLeft" => monitor.BoundsDip.Left + margin,
            _ => monitor.BoundsDip.Right - Width - margin
        };
        double top = settings.Anchor switch
        {
            "BottomLeft" or "BottomRight" => monitor.BoundsDip.Bottom - height - margin,
            _ => monitor.BoundsDip.Top + margin
        };

        Left = left;
        Top = top;
    }

    private void ApplyNativeStyles()
    {
        NativeWindowHelper.ExcludeFromCapture(this);
        NativeWindowHelper.ApplyToolWindowStyles(this);
    }

    private void OnChatCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(ScrollChatToEnd);

    private void OnEventCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(ScrollEventsToEnd);

    private void ScrollListsToEnd()
    {
        ScrollChatToEnd();
        ScrollEventsToEnd();
    }

    private void ScrollChatToEnd()
    {
        if (ChatList.Items.Count == 0)
        {
            return;
        }

        ChatList.ScrollIntoView(ChatList.Items[^1]);
    }

    private void ScrollEventsToEnd()
    {
        if (EventsList.Items.Count == 0)
        {
            return;
        }

        EventsList.ScrollIntoView(EventsList.Items[^1]);
    }

    protected override void OnClosed(EventArgs e)
    {
        _chatItems?.CollectionChanged -= OnChatCollectionChanged;

        _eventItems?.CollectionChanged -= OnEventCollectionChanged;

        base.OnClosed(e);
    }
}
