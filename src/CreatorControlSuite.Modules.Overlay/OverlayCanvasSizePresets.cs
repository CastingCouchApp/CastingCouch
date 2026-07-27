namespace CreatorControlSuite.Modules.Overlay;

public static class OverlayCanvasSizePresets
{
    public sealed record Preset(string Id, string Label, int Width, int Height);

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("1080p", "1920 × 1080 (Full HD)", 1920, 1080),
        new("720p", "1280 × 720 (HD)", 1280, 720),
        new("1440p", "2560 × 1440 (QHD)", 2560, 1440),
        new("4k", "3840 × 2160 (4K)", 3840, 2160),
        new("1080p-vert", "1080 × 1920 (Vertical)", 1080, 1920),
        new("720p-vert", "720 × 1280 (Vertical)", 720, 1280),
        new("square", "1080 × 1080 (Square)", 1080, 1080)
    ];

    public static Preset? Find(int width, int height) =>
        All.FirstOrDefault(p => p.Width == width && p.Height == height);

    public static Preset Default => All[0];

    public static bool IsValid(int width, int height) =>
        width is >= 320 and <= 7680 && height is >= 180 and <= 4320;
}
