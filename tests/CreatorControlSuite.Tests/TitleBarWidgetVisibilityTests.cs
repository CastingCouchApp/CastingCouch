using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class TitleBarWidgetVisibilityTests
{
    [Fact]
    public void KnownWidgets_ExposeStableKeysAndGermanLabels()
    {
        Assert.Equal(
            [
                TitleBarWidgetVisibility.Stream,
                TitleBarWidgetVisibility.Quality,
                TitleBarWidgetVisibility.Music,
                TitleBarWidgetVisibility.Community,
                TitleBarWidgetVisibility.Session,
                TitleBarWidgetVisibility.Countdown,
                TitleBarWidgetVisibility.Connections
            ],
            TitleBarWidgetVisibility.All.Select(item => item.Key).ToArray());

        Assert.Contains(
            TitleBarWidgetVisibility.All,
            item => item is { Key: TitleBarWidgetVisibility.Quality, Label: "Qualität" });
        Assert.Contains(
            TitleBarWidgetVisibility.All,
            item => item is { Key: TitleBarWidgetVisibility.Connections, Label: "Verbindungen" });
    }

    [Fact]
    public void IsVisible_DefaultsToTrue_WhenHiddenListMissingOrEmpty()
    {
        Assert.True(TitleBarWidgetVisibility.IsVisible(null, TitleBarWidgetVisibility.Music));
        Assert.True(TitleBarWidgetVisibility.IsVisible([], TitleBarWidgetVisibility.Music));
        Assert.True(new GeneralSettings().TitleBarHiddenWidgets.Count == 0);
    }

    [Fact]
    public void IsVisible_ReturnsFalse_WhenWidgetIsHidden()
    {
        Assert.False(TitleBarWidgetVisibility.IsVisible(
            [TitleBarWidgetVisibility.Session, TitleBarWidgetVisibility.Music],
            TitleBarWidgetVisibility.Music));
        Assert.True(TitleBarWidgetVisibility.IsVisible(
            [TitleBarWidgetVisibility.Session],
            TitleBarWidgetVisibility.Music));
    }

    [Fact]
    public void SetHidden_AddsAndRemovesKeysIdempotently()
    {
        var hidden = new List<string> { TitleBarWidgetVisibility.Stream };

        TitleBarWidgetVisibility.SetHidden(hidden, TitleBarWidgetVisibility.Music, hide: true);
        TitleBarWidgetVisibility.SetHidden(hidden, TitleBarWidgetVisibility.Music, hide: true);
        Assert.Equal(
            [TitleBarWidgetVisibility.Stream, TitleBarWidgetVisibility.Music],
            hidden);

        TitleBarWidgetVisibility.SetHidden(hidden, TitleBarWidgetVisibility.Music, hide: false);
        Assert.Equal([TitleBarWidgetVisibility.Stream], hidden);
    }

    [Fact]
    public void ShouldShowDividerBefore_OnlyWhenTargetWidgetVisible()
    {
        Assert.True(TitleBarWidgetVisibility.ShouldShowDividerBefore(
            [],
            TitleBarWidgetVisibility.Quality));
        Assert.False(TitleBarWidgetVisibility.ShouldShowDividerBefore(
            [TitleBarWidgetVisibility.Quality],
            TitleBarWidgetVisibility.Quality));
    }
}
