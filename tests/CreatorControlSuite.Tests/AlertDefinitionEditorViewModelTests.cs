using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class AlertDefinitionEditorViewModelTests
{
    [Fact]
    public void Load_MapsDefinition()
    {
        var definition = new AlertDefinitionSettings
        {
            TextTemplate = "Hello {user}",
            MediaPath = "video.mp4",
            SoundPath = "sound.wav",
            AudioOutputDeviceId = "device",
            SoundStartSeconds = 1.5,
            SoundEndSeconds = 4.25,
            DurationSeconds = 12,
            Priority = 25,
            FontFace = "Inter",
            FontSize = 48,
            FontColor = "#123456",
            Animation = "Zoom"
        };
        var viewModel = new AlertDefinitionEditorViewModel();

        viewModel.Load(definition);

        Assert.Equal("Hello {user}", viewModel.TextTemplate);
        Assert.Equal("video.mp4", viewModel.MediaPath);
        Assert.Equal("sound.wav", viewModel.SoundPath);
        Assert.Equal("device", viewModel.AudioOutputDeviceId);
        Assert.Equal(1.5, viewModel.SoundStartSeconds);
        Assert.Equal(4.25, viewModel.SoundEndSeconds);
        Assert.Equal("12", viewModel.DurationSeconds);
        Assert.Equal("25", viewModel.Priority);
        Assert.Equal("Inter", viewModel.FontFace);
        Assert.Equal("48", viewModel.FontSize);
        Assert.Equal("#123456", viewModel.FontColor);
        Assert.Equal("Zoom", viewModel.Animation);
    }

    [Fact]
    public void TryApplyTo_MapsAndNormalizesDefinition()
    {
        var viewModel = new AlertDefinitionEditorViewModel
        {
            TextTemplate = " Text ",
            MediaPath = " media.mp4 ",
            SoundPath = " sound.wav ",
            AudioOutputDeviceId = " device ",
            SoundStartSeconds = -2,
            SoundEndSeconds = 1,
            DurationSeconds = "15",
            Priority = "5",
            FontFace = " Inter ",
            FontSize = "52",
            FontColor = " #abcdef ",
            Animation = "Bounce"
        };
        var definition = new AlertDefinitionSettings();

        bool applied = viewModel.TryApplyTo(
            definition,
            out string error);

        Assert.True(applied, error);
        Assert.Equal("Text", definition.TextTemplate);
        Assert.Equal("media.mp4", definition.MediaPath);
        Assert.Equal("sound.wav", definition.SoundPath);
        Assert.Equal("device", definition.AudioOutputDeviceId);
        Assert.Equal(0, definition.SoundStartSeconds);
        Assert.Equal(1, definition.SoundEndSeconds);
        Assert.Equal(15, definition.DurationSeconds);
        Assert.Equal(5, definition.Priority);
        Assert.Equal("Inter", definition.FontFace);
        Assert.Equal(52, definition.FontSize);
        Assert.Equal("#abcdef", definition.FontColor);
        Assert.Equal("Bounce", definition.Animation);
    }

    [Theory]
    [InlineData("", "100", "44")]
    [InlineData("8", "invalid", "44")]
    [InlineData("8", "100", "invalid")]
    [InlineData("0", "100", "44")]
    public void TryApplyTo_RejectsInvalidNumericFields(
        string duration,
        string priority,
        string fontSize)
    {
        var viewModel = new AlertDefinitionEditorViewModel
        {
            DurationSeconds = duration,
            Priority = priority,
            FontSize = fontSize
        };

        bool applied = viewModel.TryApplyTo(
            new AlertDefinitionSettings(),
            out string error);

        Assert.False(applied);
        Assert.NotEmpty(error);
    }
}
