using System.Text.Json;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Tests;

public sealed class OverlayDataSerializationTests
{
    [Fact]
    public void UsesExpectedDataShape()
    {
        var data = new OverlayData();
        data.Stream.IsLive = true;
        data.Spotify.StatusText = "Pause";

        var json = JsonSerializer.Serialize(
            data,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        Assert.Contains("\"stream\"", json);
        Assert.Contains("\"spotify\"", json);
        Assert.Contains("\"isLive\":true", json);
        Assert.Contains("\"statusText\":\"Pause\"", json);
    }
}
