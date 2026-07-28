using CreatorControlSuite.App.Themes;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class GeneralSettingsPageViewModelTests
{
    [Fact]
    public void Load_MapsSettingsAndAppliesConfiguredTheme()
    {
        var themes = new FakeThemeService();
        var viewModel = new GeneralSettingsPageViewModel(themes);
        var branding = new BrandingSettings
        {
            DisplayName = "Stream",
            ChannelName = "channel"
        };
        var general = new GeneralSettings
        {
            ThemeId = "pink-cage-flair",
            TitleBarWidgetCardsEnabled = true,
            ConnectionWatchdogSeconds = 42,
            ReconnectSpotify = false
        };

        viewModel.Load(branding, general);

        Assert.Equal("Stream", viewModel.DisplayName);
        Assert.Equal("42", viewModel.ConnectionWatchdogSeconds);
        Assert.False(viewModel.ReconnectSpotify);
        Assert.True(viewModel.TitleBarWidgetCardsEnabled);
        Assert.Equal("pink-cage-flair", viewModel.SelectedTheme?.Id);
        Assert.Equal("pink-cage-flair", themes.LastAppliedThemeId);
    }

    [Fact]
    public void ApplyTo_TrimsValuesAndClampsWatchdogInterval()
    {
        var viewModel =
            new GeneralSettingsPageViewModel(new FakeThemeService())
            {
                DisplayName = " Stream ",
                ChannelName = " channel ",
                SelectedTheme = ThemeCatalog.Resolve("arctic-glass-lab"),
                TitleBarWidgetCardsEnabled = true,
                ConnectionWatchdogSeconds = "999",
                ReconnectObs = false,
                ReconnectTwitch = true
            };
        var branding = new BrandingSettings();
        var general = new GeneralSettings();

        viewModel.ApplyTo(branding, general);

        Assert.Equal("Stream", branding.DisplayName);
        Assert.Equal("channel", branding.ChannelName);
        Assert.Equal("arctic-glass-lab", general.ThemeId);
        Assert.True(general.TitleBarWidgetCardsEnabled);
        Assert.Equal(300, general.ConnectionWatchdogSeconds);
        Assert.False(general.ReconnectObs);
        Assert.True(general.ReconnectTwitch);
    }

    [Fact]
    public void SelectingTheme_AppliesItImmediately()
    {
        var themes = new FakeThemeService();
        var viewModel = new GeneralSettingsPageViewModel(themes);

        viewModel.SelectedTheme =
            ThemeCatalog.Resolve("neon-night-market");

        Assert.Equal("neon-night-market", themes.LastAppliedThemeId);
        Assert.Contains("Lime", viewModel.ThemeDescription);
    }

    [Fact]
    public void TitleBarWidgetCardsEnabled_DefaultsToFalseAndRaisesPropertyChanged()
    {
        var viewModel = new GeneralSettingsPageViewModel(new FakeThemeService());
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                changed.Add(e.PropertyName);
            }
        };

        Assert.False(viewModel.TitleBarWidgetCardsEnabled);
        Assert.False(new GeneralSettings().TitleBarWidgetCardsEnabled);

        viewModel.TitleBarWidgetCardsEnabled = true;

        Assert.True(viewModel.TitleBarWidgetCardsEnabled);
        Assert.Contains(
            nameof(GeneralSettingsPageViewModel.TitleBarWidgetCardsEnabled),
            changed);
    }

    private sealed class FakeThemeService : IThemeSelectionService
    {
        public string? LastAppliedThemeId { get; private set; }

        public IReadOnlyList<ThemeDefinition> Themes => ThemeCatalog.All;

        public ThemeDefinition Apply(string? themeId)
        {
            LastAppliedThemeId = themeId;
            return ThemeCatalog.Resolve(themeId);
        }
    }
}
