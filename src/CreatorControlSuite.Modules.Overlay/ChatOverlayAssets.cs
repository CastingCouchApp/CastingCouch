using System.Reflection;

namespace CreatorControlSuite.Modules.Overlay;

public static class ChatOverlayAssets
{
    private static readonly Assembly Assembly = typeof(ChatOverlayAssets).Assembly;

    public static bool TryGet(string fileName, out string content, out string contentType)
    {
        content = "";
        contentType = "application/octet-stream";

        string safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return false;
        }

        string? resourceName = Assembly.GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.EndsWith(".ChatOverlay." + safeName, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".ChatOverlay." + safeName.Replace('-', '_'), StringComparison.OrdinalIgnoreCase));

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
        contentType = safeName.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            ? "text/css; charset=utf-8"
            : safeName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                ? "application/javascript; charset=utf-8"
                : "text/html; charset=utf-8";
        return true;
    }
}
