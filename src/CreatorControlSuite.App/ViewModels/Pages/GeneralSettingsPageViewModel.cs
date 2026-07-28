using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Themes;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class GeneralSettingsPageViewModel(
    IThemeSelectionService themeService) : ViewModelBase
{
    private readonly IThemeSelectionService _themeService = themeService;
    private bool _loading;

    public IReadOnlyList<ThemeDefinition> Themes => _themeService.Themes;

    public string DisplayName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string ChannelName
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public bool StartWithWindows
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool MinimizeToTray
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ThemeDefinition? SelectedTheme
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ThemeDescription));
            if (!_loading && value is not null)
            {
                _themeService.Apply(value.Id);
            }
        }
    }

    public string ThemeDescription => SelectedTheme?.Description ?? "";

    public bool TitleBarWidgetCardsEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ConnectionWatchdogEnabled
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string ConnectionWatchdogSeconds
    {
        get;
        set => SetProperty(ref field, value);
    } = "15";

    public bool ReconnectObs
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ReconnectTwitch
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ReconnectSpotify
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ReconnectYouTubeMusic
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool ReconnectStreamerBot
    {
        get;
        set => SetProperty(ref field, value);
    }

    public void Load(BrandingSettings branding, GeneralSettings general)
    {
        _loading = true;
        try
        {
            DisplayName = branding.DisplayName;
            ChannelName = branding.ChannelName;
            StartWithWindows = general.StartWithWindows;
            MinimizeToTray = general.MinimizeToTray;
            SelectedTheme = ThemeCatalog.Resolve(general.ThemeId);
            TitleBarWidgetCardsEnabled = general.TitleBarWidgetCardsEnabled;
            ConnectionWatchdogEnabled =
                general.ConnectionWatchdogEnabled;
            ConnectionWatchdogSeconds =
                general.ConnectionWatchdogSeconds.ToString();
            ReconnectObs = general.ReconnectObs;
            ReconnectTwitch = general.ReconnectTwitch;
            ReconnectSpotify = general.ReconnectSpotify;
            ReconnectYouTubeMusic = general.ReconnectYouTubeMusic;
            ReconnectStreamerBot = general.ReconnectStreamerBot;
        }
        finally
        {
            _loading = false;
        }

        _themeService.Apply(SelectedTheme?.Id);
    }

    public void ApplyTo(BrandingSettings branding, GeneralSettings general)
    {
        branding.DisplayName = DisplayName.Trim();
        branding.ChannelName = ChannelName.Trim();
        general.StartWithWindows = StartWithWindows;
        general.MinimizeToTray = MinimizeToTray;
        general.ThemeId =
            ThemeCatalog.Resolve(SelectedTheme?.Id).Id;
        general.TitleBarWidgetCardsEnabled = TitleBarWidgetCardsEnabled;
        general.ConnectionWatchdogEnabled =
            ConnectionWatchdogEnabled;
        if (int.TryParse(
                ConnectionWatchdogSeconds.Trim(),
                out int watchdogSeconds))
        {
            general.ConnectionWatchdogSeconds =
                Math.Clamp(watchdogSeconds, 5, 300);
        }

        ReconnectSettings(general);
    }

    private void ReconnectSettings(GeneralSettings general)
    {
        general.ReconnectObs = ReconnectObs;
        general.ReconnectTwitch = ReconnectTwitch;
        general.ReconnectSpotify = ReconnectSpotify;
        general.ReconnectYouTubeMusic = ReconnectYouTubeMusic;
        general.ReconnectStreamerBot = ReconnectStreamerBot;
    }
}
