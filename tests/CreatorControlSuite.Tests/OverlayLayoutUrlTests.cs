using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class OverlayLayoutUrlTests
{
    [Fact]
    public void GetEditorUrl_BuildsPath()
    {
        var settings = new OverlaySettings { WebServerPort = 8765 };
        Assert.Equal(
            "http://127.0.0.1:8765/editor/abc123",
            settings.GetEditorUrl("abc123"));
    }

    [Fact]
    public void GetViewUrl_BuildsPath()
    {
        var settings = new OverlaySettings { WebServerPort = 9000 };
        Assert.Equal(
            "http://127.0.0.1:9000/view/xyz",
            settings.GetViewUrl("xyz"));
    }

    [Fact]
    public void GetWidgetUrl_BuildsPath()
    {
        var settings = new OverlaySettings { WebServerPort = 8765 };
        Assert.Equal(
            "http://127.0.0.1:8765/w/spotify",
            settings.GetWidgetUrl("spotify"));
        Assert.Equal(
            "http://127.0.0.1:8765/w/shape/frame",
            settings.GetWidgetUrl("shape/frame"));
    }

    [Fact]
    public void GetEditorAndViewUrl_FallBackToDefaultWhenCanvasIdEmpty()
    {
        var settings = new OverlaySettings { WebServerPort = 8765 };
        Assert.Equal(
            "http://127.0.0.1:8765/editor/default",
            settings.GetEditorUrl("  "));
        Assert.Equal(
            "http://127.0.0.1:8765/view/default",
            settings.GetViewUrl(null));
    }
}
