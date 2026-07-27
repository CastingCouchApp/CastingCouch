using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class OverlaySettingsMigrationTests
{
    [Fact]
    public void EnsureInstancesMigrated_IsNoOp()
    {
        var settings = new OverlaySettings
        {
            RootPath = @"D:\Overlays\Main",
            Instances = []
        };

        settings.EnsureInstancesMigrated();
        Assert.Empty(settings.Instances);
    }

    [Fact]
    public void EnsureCanvasesMigrated_SeedsDefaultCanvas()
    {
        var settings = new OverlaySettings();
        Assert.Empty(settings.Canvases);

        settings.EnsureCanvasesMigrated();

        Assert.Single(settings.Canvases);
        Assert.Equal(OverlaySettings.DefaultCanvasId, settings.Canvases[0].Id);
        Assert.Equal("Canvas", settings.Canvases[0].Name);
        Assert.Equal(OverlaySettings.DefaultCanvasId, settings.SelectedCanvasId);
    }

    [Fact]
    public void EnsureCanvasesMigrated_KeepsExistingAndFixesSelection()
    {
        var settings = new OverlaySettings
        {
            Canvases =
            [
                new OverlayCanvasSettings { Id = "gameplay", Name = "Gameplay" },
                new OverlayCanvasSettings { Id = "chat", Name = "Just Chatting" }
            ],
            SelectedCanvasId = "missing"
        };

        settings.EnsureCanvasesMigrated();

        Assert.Equal(2, settings.Canvases.Count);
        Assert.Equal("gameplay", settings.SelectedCanvasId);
    }

    [Fact]
    public void CreateCanvasId_SlugsNameAndAvoidsCollisions()
    {
        Assert.Equal("just-chatting", OverlaySettings.CreateCanvasId("Just Chatting", []));
        Assert.Equal(
            "just-chatting-2",
            OverlaySettings.CreateCanvasId("Just Chatting", ["just-chatting"]));
        Assert.Equal("canvas", OverlaySettings.CreateCanvasId("@@@", []));
        Assert.Equal(
            "canvas-2",
            OverlaySettings.CreateCanvasId("@@@", ["canvas"]));
    }

    [Fact]
    public void GetSelectedCanvas_ReturnsSelectedEntry()
    {
        var settings = new OverlaySettings
        {
            Canvases =
            [
                new OverlayCanvasSettings { Id = "default", Name = "Canvas" },
                new OverlayCanvasSettings { Id = "game", Name = "Gameplay" }
            ],
            SelectedCanvasId = "game"
        };

        settings.EnsureCanvasesMigrated();
        OverlayCanvasSettings selected = settings.GetSelectedCanvas();
        Assert.Equal("game", selected.Id);
        Assert.Equal("Gameplay", selected.Name);
    }

    [Fact]
    public void GetEditorAndViewUrl_UseDefaultCanvas()
    {
        var settings = new OverlaySettings { WebServerPort = 8765 };
        Assert.Equal(
            "http://127.0.0.1:8765/editor/default",
            settings.GetEditorUrl());
        Assert.Equal(
            "http://127.0.0.1:8765/view/default",
            settings.GetViewUrl());
        Assert.Equal(OverlaySettings.DefaultCanvasId, "default");
    }

    [Fact]
    public void GetWidgetUrl_BuildsPath()
    {
        var settings = new OverlaySettings { WebServerPort = 8765 };
        Assert.Equal(
            "http://127.0.0.1:8765/w/music",
            settings.GetWidgetUrl("music"));
    }
}
