using System.Globalization;
using System.Windows.Media;
using CreatorControlSuite.App;

namespace CreatorControlSuite.Tests;

public sealed class TwitchRoleToBrushConverterTests
{
    private readonly TwitchRoleToBrushConverter _converter = new();

    [Theory]
    [InlineData("[STREAMER] Alice: hi", 255, 92, 92)]
    [InlineData("[MOD] Bob: hi", 87, 214, 141)]
    [InlineData("[VIP] Carol: hi", 232, 121, 249)]
    [InlineData("[SUB] Dave: hi", 167, 139, 250)]
    public void Convert_MapsRoleTagsToExpectedColors(
        string text,
        byte r,
        byte g,
        byte b)
    {
        object result = _converter.Convert(
            text,
            typeof(Brush),
            parameter: null!,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.FromRgb(r, g, b), brush.Color);
    }

    [Fact]
    public void Convert_WithoutRoleTag_ReturnsBrush()
    {
        object result = _converter.Convert(
            "Viewer: hello",
            typeof(Brush),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<Brush>(result);
    }
}
