using System.Collections.ObjectModel;
using System.Windows;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.Hud;

public sealed class StreamerHudService
{
    private StreamerHudWindow? _window;
    private ObservableCollection<string>? _chatItems;
    private ObservableCollection<string>? _eventItems;
    private string _liveStatus = "OFFLINE";
    private StreamerHudSettings _settings = new();

    public bool IsVisible => _window is { IsVisible: true };

    public void BindSources(ObservableCollection<string> chatItems, ObservableCollection<string> eventItems)
    {
        _chatItems = chatItems;
        _eventItems = eventItems;
        _window?.BindSources(chatItems, eventItems);
    }

    public void Apply(StreamerHudSettings settings, bool forceShow = false)
    {
        _settings = Clone(settings);
        if (!_settings.Enabled && !forceShow)
        {
            Hide();
            return;
        }

        EnsureWindow();
        _window!.BindSources(_chatItems ?? [], _eventItems ?? []);
        _window.UpdateLiveStatus(_liveStatus);
        _window.ApplySettings(_settings);
        if (!_window.IsVisible)
        {
            _window.Show();
        }
    }

    public void ShowPreview(StreamerHudSettings settings)
    {
        StreamerHudSettings preview = Clone(settings);
        preview.Enabled = true;
        Apply(preview, forceShow: true);
    }

    public void Hide()
    {
        if (_window is null)
        {
            return;
        }

        _window.Hide();
    }

    public void Close()
    {
        if (_window is null)
        {
            return;
        }

        _window.Close();
        _window = null;
    }

    public void UpdateLiveStatus(string text)
    {
        _liveStatus = string.IsNullOrWhiteSpace(text) ? "OFFLINE" : text;
        _window?.UpdateLiveStatus(_liveStatus);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new StreamerHudWindow();
        _window.Closed += (_, _) => _window = null;
        if (_chatItems is not null && _eventItems is not null)
        {
            _window.BindSources(_chatItems, _eventItems);
        }
    }

    private static StreamerHudSettings Clone(StreamerHudSettings source) => new()
    {
        Enabled = source.Enabled,
        MonitorIndex = source.MonitorIndex,
        Opacity = source.Opacity,
        ClickThrough = source.ClickThrough,
        ShowChat = source.ShowChat,
        ShowEvents = source.ShowEvents,
        ShowLiveStatus = source.ShowLiveStatus,
        Anchor = source.Anchor,
        Margin = source.Margin,
        PanelWidth = source.PanelWidth
    };
}
