using System.Reflection;

namespace CreatorControlSuite.Modules.Overlay;

public static class CanvasOverlayAssets
{
    private static readonly Assembly Assembly = typeof(CanvasOverlayAssets).Assembly;
    private const string Marker = ".CanvasOverlay.";

    public static bool TryGet(string relativePath, out string content, out string contentType)
    {
        content = "";
        contentType = "application/octet-stream";

        string normalized = (relativePath ?? "")
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        string dotted = normalized.Replace('/', '.');
        string? resourceName = Assembly.GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.Contains(Marker, StringComparison.OrdinalIgnoreCase) &&
                (name.EndsWith(Marker + dotted, StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith("." + dotted, StringComparison.OrdinalIgnoreCase)));

        if (resourceName is null)
        {
            // Fallback: match by filename only for flat lookups
            string fileName = Path.GetFileName(normalized);
            resourceName = Assembly.GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.Contains(Marker, StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        }

        if (resourceName is null)
        {
            return false;
        }

        using Stream? stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return false;
        }

        using var reader = new StreamReader(stream);
        content = reader.ReadToEnd();
        contentType = GuessContentType(normalized);
        return true;
    }

    public static IReadOnlyList<string> ListWidgetTypes() =>
    [
        "online",
        "alert",
        "music",
        "chat",
        "ending-stats",
        "text",
        "image",
        "countdown",
        "socials"
    ];

    public static IReadOnlyList<string> ListShapeTypes() =>
    [
        "frame",
        "frame.card",
        "shape.vignette",
        "shape.scene-bg",
        "shape.cutout"
    ];

    private static string GuessContentType(string path)
    {
        if (path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            return "text/css; charset=utf-8";
        }

        if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            return "application/javascript; charset=utf-8";
        }

        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return "text/html; charset=utf-8";
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return "application/json; charset=utf-8";
        }

        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return "image/svg+xml";
        }

        return "application/octet-stream";
    }
}
