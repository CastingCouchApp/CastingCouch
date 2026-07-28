using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class OverlayConnectionSettingsPageViewModel : ViewModelBase
{
    public OverlayConnectionSettingsPageViewModel()
    {
        CopyBaseUrlCommand = new RelayCommand(
            () => Copy(BaseUrl, "Base-URL kopiert."));
        CopyChatUrlCommand = new RelayCommand(
            () => Copy(ChatUrl, "Chat-URL kopiert."));
        BrowseBackgroundCommand = new AsyncRelayCommand(
            _ => BrowseBackgroundAsync());
    }

    public bool WebServerEnabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public string WebServerPort
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RefreshUrls();
            }
        }
    } = "8765";

    public string BaseUrl
    {
        get;
        private set => SetProperty(ref field, value);
    } = "http://127.0.0.1:8765";

    public string ServerStatus
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Webserver: –";

    public bool ChatEnabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool ShowTwitchEvents
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool EnableBttv
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool EnableFfz
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool EnableSevenTv
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public string BackgroundType
    {
        get;
        set => SetProperty(ref field, value);
    } = "None";

    public string BackgroundColor
    {
        get;
        set => SetProperty(ref field, value);
    } = "#000000";

    public string BackgroundImagePath
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string BackgroundOpacityPercent
    {
        get;
        set => SetProperty(ref field, value);
    } = "55";

    public string PaddingPx
    {
        get;
        set => SetProperty(ref field, value);
    } = "12";

    public string BorderRadiusPx
    {
        get;
        set => SetProperty(ref field, value);
    } = "12";

    public string GapPx
    {
        get;
        set => SetProperty(ref field, value);
    } = "6";

    public string FontSizePx
    {
        get;
        set => SetProperty(ref field, value);
    } = "18";

    public string FontFamily
    {
        get;
        set => SetProperty(ref field, value);
    } = "Segoe UI, system-ui, sans-serif";

    public string ChatUrl
    {
        get;
        private set => SetProperty(ref field, value);
    } = "http://127.0.0.1:8765/chat";

    public RelayCommand CopyBaseUrlCommand { get; }

    public RelayCommand CopyChatUrlCommand { get; }

    public AsyncRelayCommand BrowseBackgroundCommand { get; }

    public Action<string>? CopyTextRequested { get; set; }

    public Func<Task<string?>>? BrowseBackgroundRequestedAsync { get; set; }

    public void Load(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Chat ??= new OverlayChatSettings();
        settings.Chat.NormalizeAppearance();

        WebServerEnabled = settings.WebServerEnabled;
        WebServerPort = settings.WebServerPort.ToString();
        ChatEnabled = settings.Chat.Enabled;
        ShowTwitchEvents = settings.Chat.ShowTwitchEvents;
        EnableBttv = settings.Chat.EnableBttv;
        EnableFfz = settings.Chat.EnableFfz;
        EnableSevenTv = settings.Chat.EnableSevenTv;
        BackgroundType = settings.Chat.BackgroundType;
        BackgroundColor = settings.Chat.BackgroundColor;
        BackgroundImagePath = settings.Chat.BackgroundImagePath;
        BackgroundOpacityPercent =
            ((int)Math.Round(settings.Chat.BackgroundOpacity * 100)).ToString();
        PaddingPx = settings.Chat.PaddingPx.ToString();
        BorderRadiusPx = settings.Chat.BorderRadiusPx.ToString();
        GapPx = settings.Chat.GapPx.ToString();
        FontSizePx = settings.Chat.FontSizePx.ToString();
        FontFamily = settings.Chat.FontFamily;
        BaseUrl = settings.GetBaseUrl();
        ChatUrl = settings.GetOverlayUrl("chat");
    }

    public bool TryApplyTo(
        OverlaySettings settings,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!int.TryParse(WebServerPort.Trim(), out int port) ||
            port is <= 0 or > 65535)
        {
            error = "Ungültiger Overlay-Webserver-Port.";
            return false;
        }

        settings.WebServerEnabled = WebServerEnabled;
        settings.WebServerPort = port;
        settings.Chat ??= new OverlayChatSettings();
        settings.Chat.Enabled = ChatEnabled;
        settings.Chat.ShowTwitchEvents = ShowTwitchEvents;
        settings.Chat.EnableBttv = EnableBttv;
        settings.Chat.EnableFfz = EnableFfz;
        settings.Chat.EnableSevenTv = EnableSevenTv;
        settings.Chat.BackgroundType = BackgroundType;
        settings.Chat.BackgroundColor = BackgroundColor.Trim();
        settings.Chat.BackgroundImagePath = BackgroundImagePath.Trim();
        ApplyInt(
            BackgroundOpacityPercent,
            value => settings.Chat.BackgroundOpacity = value / 100d);
        ApplyInt(PaddingPx, value => settings.Chat.PaddingPx = value);
        ApplyInt(
            BorderRadiusPx,
            value => settings.Chat.BorderRadiusPx = value);
        ApplyInt(GapPx, value => settings.Chat.GapPx = value);
        ApplyInt(FontSizePx, value => settings.Chat.FontSizePx = value);
        settings.Chat.FontFamily = FontFamily.Trim();
        settings.Chat.NormalizeAppearance();

        error = "";
        Load(settings);
        return true;
    }

    public void UpdateServerStatus(string status) =>
        ServerStatus = status;

    private void Copy(string text, string successStatus)
    {
        try
        {
            CopyTextRequested?.Invoke(text);
            ServerStatus = successStatus;
        }
        catch (Exception exception)
        {
            ServerStatus = "URL konnte nicht kopiert werden: " +
                           exception.Message;
        }
    }

    private async Task BrowseBackgroundAsync()
    {
        if (BrowseBackgroundRequestedAsync is null)
        {
            return;
        }

        string? selected = await BrowseBackgroundRequestedAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        BackgroundImagePath = selected;
        BackgroundType = "Image";
    }

    private void RefreshUrls()
    {
        if (!int.TryParse(WebServerPort.Trim(), out int port) ||
            port is <= 0 or > 65535)
        {
            return;
        }

        BaseUrl = $"http://127.0.0.1:{port}";
        ChatUrl = BaseUrl + "/chat";
    }

    private static void ApplyInt(
        string text,
        Action<int> apply)
    {
        if (int.TryParse(text.Trim(), out int value))
        {
            apply(value);
        }
    }
}
