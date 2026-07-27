using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class OverlayChatAppearanceSettingsTests
{
    [Fact]
    public void NormalizeAppearance_ClampsAndNormalizesValues()
    {
        var chat = new OverlayChatSettings
        {
            BackgroundType = "color",
            BackgroundOpacity = 2.5,
            PaddingPx = 999,
            BorderRadiusPx = -3,
            GapPx = 100,
            BackgroundColor = "  #112233  ",
            MaxBufferedMessages = 5000
        };

        chat.NormalizeAppearance();

        Assert.Equal("Color", chat.BackgroundType);
        Assert.Equal(1, chat.BackgroundOpacity);
        Assert.Equal(120, chat.PaddingPx);
        Assert.Equal(0, chat.BorderRadiusPx);
        Assert.Equal(48, chat.GapPx);
        Assert.Equal("#112233", chat.BackgroundColor);
        Assert.Equal(1000, chat.MaxBufferedMessages);
    }

    [Theory]
    [InlineData(null, "None")]
    [InlineData("", "None")]
    [InlineData("image", "Image")]
    [InlineData("IMAGE", "Image")]
    [InlineData("foo", "None")]
    public void NormalizeAppearance_MapsBackgroundType(string? input, string expected)
    {
        var chat = new OverlayChatSettings { BackgroundType = input! };
        chat.NormalizeAppearance();
        Assert.Equal(expected, chat.BackgroundType);
    }
}
