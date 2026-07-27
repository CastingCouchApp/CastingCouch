using System.Text.Json;
using CreatorControlSuite.Modules.OBS.Models;

namespace CreatorControlSuite.Tests;

public sealed class ObsVideoSettingsTests
{
    [Fact]
    public void Parse_ReadsBaseAndOutputDimensions()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "baseWidth": 2560,
              "baseHeight": 1440,
              "outputWidth": 1920,
              "outputHeight": 1080,
              "fpsNumerator": 60,
              "fpsDenominator": 1
            }
            """);

        ObsVideoSettings settings = ObsVideoSettings.Parse(doc.RootElement);

        Assert.Equal(2560, settings.BaseWidth);
        Assert.Equal(1440, settings.BaseHeight);
        Assert.Equal(1920, settings.OutputWidth);
        Assert.Equal(1080, settings.OutputHeight);
        Assert.Equal(60, settings.FpsNumerator);
        Assert.Equal(1, settings.FpsDenominator);
    }

    [Fact]
    public void Parse_MissingFields_DefaultsToZero()
    {
        using var doc = JsonDocument.Parse("{}");

        ObsVideoSettings settings = ObsVideoSettings.Parse(doc.RootElement);

        Assert.Equal(0, settings.BaseWidth);
        Assert.Equal(0, settings.BaseHeight);
        Assert.Equal(0, settings.OutputWidth);
        Assert.Equal(0, settings.OutputHeight);
    }
}
