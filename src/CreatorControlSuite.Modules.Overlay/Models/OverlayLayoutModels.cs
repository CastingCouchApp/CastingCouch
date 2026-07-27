using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreatorControlSuite.Modules.Overlay.Models;

public sealed class OverlayLayout
{
    public int Version { get; set; } = 1;
    /// <summary>Anzeigename; Quelle der Wahrheit für die Canvas-Liste bleibt OverlaySettings.Canvases.</summary>
    public string Name { get; set; } = "";
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OverlayLayoutItem> Items { get; set; } = [];

    public static OverlayLayout CreateDefault() => new()
    {
        Version = 1,
        Name = "",
        CanvasWidth = 1920,
        CanvasHeight = 1080,
        UpdatedAt = DateTimeOffset.UtcNow,
        Items = []
    };
}

public sealed class OverlayLayoutItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "widget";
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 200;
    public double H { get; set; } = 100;
    public int Z { get; set; }
    public double Rotation { get; set; }
    public bool Locked { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public Dictionary<string, JsonElement> Props { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
