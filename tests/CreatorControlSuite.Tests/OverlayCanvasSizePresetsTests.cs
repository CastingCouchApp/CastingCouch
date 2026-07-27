using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.Tests;

public sealed class OverlayCanvasSizePresetsTests
{
    [Fact]
    public void All_Contains1080pDefault()
    {
        Assert.Equal(1920, OverlayCanvasSizePresets.Default.Width);
        Assert.Equal(1080, OverlayCanvasSizePresets.Default.Height);
        Assert.Contains(OverlayCanvasSizePresets.All, p => p.Id == "1080p");
    }

    [Fact]
    public void Find_MatchesExactSize()
    {
        OverlayCanvasSizePresets.Preset? preset = OverlayCanvasSizePresets.Find(1280, 720);
        Assert.NotNull(preset);
        Assert.Equal("720p", preset!.Id);
    }

    [Theory]
    [InlineData(1920, 1080, true)]
    [InlineData(100, 100, false)]
    [InlineData(8000, 1080, false)]
    public void IsValid_EnforcesBounds(int w, int h, bool expected)
    {
        Assert.Equal(expected, OverlayCanvasSizePresets.IsValid(w, h));
    }
}
