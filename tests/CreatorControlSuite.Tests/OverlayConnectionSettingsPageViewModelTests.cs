using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class OverlayConnectionSettingsPageViewModelTests
{
    [Fact]
    public void Load_MapsServerAndChatSettings()
    {
        var settings = new OverlaySettings
        {
            WebServerEnabled = false,
            WebServerPort = 9876,
            Chat =
            {
                Enabled = false,
                ShowTwitchEvents = false,
                EnableBttv = false,
                BackgroundType = "Image",
                BackgroundColor = "#123456",
                BackgroundImagePath = "background.png",
                BackgroundOpacity = 0.42,
                PaddingPx = 21,
                BorderRadiusPx = 17,
                GapPx = 9,
                FontSizePx = 24,
                FontFamily = "Inter"
            }
        };
        var viewModel = new OverlayConnectionSettingsPageViewModel();

        viewModel.Load(settings);

        Assert.False(viewModel.WebServerEnabled);
        Assert.Equal("9876", viewModel.WebServerPort);
        Assert.Equal("http://127.0.0.1:9876", viewModel.BaseUrl);
        Assert.Equal(
            "http://127.0.0.1:9876/chat",
            viewModel.ChatUrl);
        Assert.False(viewModel.ChatEnabled);
        Assert.False(viewModel.ShowTwitchEvents);
        Assert.False(viewModel.EnableBttv);
        Assert.Equal("Image", viewModel.BackgroundType);
        Assert.Equal("42", viewModel.BackgroundOpacityPercent);
        Assert.Equal("Inter", viewModel.FontFamily);
    }

    [Fact]
    public void TryApplyTo_NormalizesAppearance()
    {
        var viewModel = new OverlayConnectionSettingsPageViewModel
        {
            WebServerEnabled = true,
            WebServerPort = "9000",
            ChatEnabled = true,
            BackgroundType = "unknown",
            BackgroundColor = " ",
            BackgroundOpacityPercent = "150",
            PaddingPx = "-5",
            BorderRadiusPx = "80",
            GapPx = "99",
            FontSizePx = "4",
            FontFamily = " "
        };
        var settings = new OverlaySettings();

        bool applied = viewModel.TryApplyTo(
            settings,
            out string error);

        Assert.True(applied, error);
        Assert.Equal(9000, settings.WebServerPort);
        Assert.Equal("None", settings.Chat.BackgroundType);
        Assert.Equal("#000000", settings.Chat.BackgroundColor);
        Assert.Equal(1, settings.Chat.BackgroundOpacity);
        Assert.Equal(0, settings.Chat.PaddingPx);
        Assert.Equal(64, settings.Chat.BorderRadiusPx);
        Assert.Equal(48, settings.Chat.GapPx);
        Assert.Equal(8, settings.Chat.FontSizePx);
        Assert.Equal(
            "Segoe UI, system-ui, sans-serif",
            settings.Chat.FontFamily);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void TryApplyTo_RejectsInvalidPort(string port)
    {
        var viewModel = new OverlayConnectionSettingsPageViewModel
        {
            WebServerPort = port
        };

        bool applied = viewModel.TryApplyTo(
            new OverlaySettings(),
            out string error);

        Assert.False(applied);
        Assert.Contains("Port", error);
    }

    [Fact]
    public async Task Commands_DelegateCopyAndBackgroundSelection()
    {
        var viewModel = new OverlayConnectionSettingsPageViewModel();
        viewModel.Load(new OverlaySettings
        {
            WebServerPort = 8765
        });
        string? copied = null;
        viewModel.CopyTextRequested = text => copied = text;
        viewModel.BrowseBackgroundRequestedAsync =
            () => Task.FromResult<string?>("selected.png");

        viewModel.CopyChatUrlCommand.Execute(null);
        Assert.Equal(
            "http://127.0.0.1:8765/chat",
            copied);

        viewModel.BrowseBackgroundCommand.Execute(null);
        await WaitUntilAsync(
            () => viewModel.BackgroundImagePath == "selected.png");

        Assert.Equal("Image", viewModel.BackgroundType);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 20 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }
}
