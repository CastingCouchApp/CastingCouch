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
        data.Countdown.IsRunning = true;
        data.Countdown.RemainingSeconds = 90;
        data.Countdown.TotalSeconds = 600;
        data.Countdown.Label = "Stream startet";
        data.Countdown.Mode = "stream-start";

        string json = JsonSerializer.Serialize(
            data,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        Assert.Contains("\"stream\"", json);
        Assert.Contains("\"spotify\"", json);
        Assert.Contains("\"countdown\"", json);
        Assert.Contains("\"isLive\":true", json);
        Assert.Contains("\"statusText\":\"Pause\"", json);
        Assert.Contains("\"remainingSeconds\":90", json);
        Assert.Contains("\"label\":\"Stream startet\"", json);
    }
}
