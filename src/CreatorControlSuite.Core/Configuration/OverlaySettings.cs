namespace CreatorControlSuite.Core.Configuration;

public sealed class OverlayChatSettings
{
    public bool Enabled { get; set; } = true;
    public bool EnableBttv { get; set; } = true;
    public bool EnableFfz { get; set; } = true;
    public bool EnableSevenTv { get; set; } = true;
    public bool ShowTwitchEvents { get; set; } = true;
    public int MaxBufferedMessages { get; set; } = 100;

    /// <summary>None | Color | Image</summary>
    public string BackgroundType { get; set; } = "None";
    public string BackgroundColor { get; set; } = "#000000";
    public string BackgroundImagePath { get; set; } = "";
    /// <summary>0..1 – nur Hintergrundschicht, Text bleibt deckend.</summary>
    public double BackgroundOpacity { get; set; } = 0.55;
    public int PaddingPx { get; set; } = 12;
    public int BorderRadiusPx { get; set; } = 12;
    public int GapPx { get; set; } = 6;
    public int FontSizePx { get; set; } = 18;
    public string FontFamily { get; set; } = "Segoe UI, system-ui, sans-serif";

    public void NormalizeAppearance()
    {
        string type = (BackgroundType ?? "None").Trim();
        BackgroundType = type.Equals("Color", StringComparison.OrdinalIgnoreCase) ? "Color"
            : type.Equals("Image", StringComparison.OrdinalIgnoreCase) ? "Image"
            : "None";

        BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor)
            ? "#000000"
            : BackgroundColor.Trim();
        BackgroundImagePath = (BackgroundImagePath ?? "").Trim();
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 1);
        PaddingPx = Math.Clamp(PaddingPx, 0, 120);
        BorderRadiusPx = Math.Clamp(BorderRadiusPx, 0, 64);
        GapPx = Math.Clamp(GapPx, 0, 48);
        FontSizePx = Math.Clamp(FontSizePx, 8, 72);
        FontFamily = string.IsNullOrWhiteSpace(FontFamily)
            ? "Segoe UI, system-ui, sans-serif"
            : FontFamily.Trim();
        MaxBufferedMessages = Math.Clamp(MaxBufferedMessages, 0, 1000);
    }
}

public sealed class OverlayInstanceSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Legacy: früher HTML-Ordner für /o/{id}/. Wird nicht mehr genutzt.</summary>
    public string RootPath { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class OverlayCanvasSettings
{
    public string Id { get; set; } = OverlaySettings.DefaultCanvasId;
    public string Name { get; set; } = "Canvas";
}

public sealed class OverlaySettings
{
    public const string DefaultCanvasId = "default";

    /// <summary>
    /// Optionaler Datenroot für overlay-data.json. Leer = %LocalAppData%\CreatorControlSuite\Overlay.
    /// </summary>
    public string RootPath { get; set; } = "";
    public string DataFileName { get; set; } = "overlay-data.json";
    // Optionaler vollständiger Pfad zu der JSON-Datei, die vorhandene Overlays bereits lesen.
    // Leer bedeutet: automatische Standarddatei unter %LocalAppData%\CreatorControlSuite\Overlay\data.
    public string DataFilePath { get; set; } = "";
    // Rückwärtskompatibel für ältere Einstellungsdateien; wird nicht mehr geschrieben.
    public List<string> AdditionalDataRoots { get; set; } = [];
    /// <summary>Legacy-Liste; Ordner-Mounts unter /o/{id}/ entfallen.</summary>
    public List<OverlayInstanceSettings> Instances { get; set; } = [];
    /// <summary>Benannte Overlay-Canvases (Layout pro Id unter /view/{id}).</summary>
    public List<OverlayCanvasSettings> Canvases { get; set; } = [];
    /// <summary>In der Overlay-UI ausgewähltes Canvas (kein Live-Switch der View-URL).</summary>
    public string SelectedCanvasId { get; set; } = DefaultCanvasId;
    public bool WebServerEnabled { get; set; } = true;
    public int WebServerPort { get; set; } = 8765;
    public OverlayChatSettings Chat { get; set; } = new();

    public string GetBaseUrl() => $"http://127.0.0.1:{Math.Clamp(WebServerPort, 1, 65535)}";

    public string GetEditorUrl(string? canvasId = null)
    {
        string id = NormalizeCanvasIdForUrl(canvasId);
        return $"{GetBaseUrl()}/editor/{Uri.EscapeDataString(id)}";
    }

    public string GetViewUrl(string? canvasId = null)
    {
        string id = NormalizeCanvasIdForUrl(canvasId);
        return $"{GetBaseUrl()}/view/{Uri.EscapeDataString(id)}";
    }

    private static string NormalizeCanvasIdForUrl(string? canvasId)
    {
        string id = string.IsNullOrWhiteSpace(canvasId) ? DefaultCanvasId : canvasId.Trim().Trim('/');
        return string.IsNullOrWhiteSpace(id) ? DefaultCanvasId : id;
    }

    public string GetWidgetUrl(string typeOrPath)
    {
        string path = (typeOrPath ?? "")
            .Replace('\\', '/')
            .Trim()
            .Trim('/');
        return string.IsNullOrWhiteSpace(path)
            ? $"{GetBaseUrl()}/w"
            : $"{GetBaseUrl()}/w/{path}";
    }

    public string GetOverlayUrl(string relativePath)
    {
        string rel = (relativePath ?? "")
            .Replace('\\', '/')
            .TrimStart('/');
        return string.IsNullOrWhiteSpace(rel)
            ? GetBaseUrl()
            : $"{GetBaseUrl()}/{rel}";
    }

    /// <summary>No-op: Instanzordner entfallen; bleibt für ältere Call-Sites.</summary>
    public void EnsureInstancesMigrated()
    {
        Instances ??= [];
    }

    public void EnsureCanvasesMigrated()
    {
        Canvases ??= [];
        if (Canvases.Count == 0)
        {
            Canvases.Add(new OverlayCanvasSettings
            {
                Id = DefaultCanvasId,
                Name = "Canvas"
            });
        }

        for (int i = Canvases.Count - 1; i >= 0; i--)
        {
            OverlayCanvasSettings canvas = Canvases[i];
            if (canvas is null || string.IsNullOrWhiteSpace(canvas.Id))
            {
                Canvases.RemoveAt(i);
                continue;
            }

            canvas.Id = canvas.Id.Trim();
            if (string.IsNullOrWhiteSpace(canvas.Name))
            {
                canvas.Name = canvas.Id;
            }
        }

        if (Canvases.Count == 0)
        {
            Canvases.Add(new OverlayCanvasSettings
            {
                Id = DefaultCanvasId,
                Name = "Canvas"
            });
        }

        if (string.IsNullOrWhiteSpace(SelectedCanvasId) ||
            !Canvases.Any(c => string.Equals(c.Id, SelectedCanvasId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedCanvasId = Canvases[0].Id;
        }
    }

    public OverlayCanvasSettings GetSelectedCanvas()
    {
        EnsureCanvasesMigrated();
        return Canvases.FirstOrDefault(c =>
                   string.Equals(c.Id, SelectedCanvasId, StringComparison.OrdinalIgnoreCase))
               ?? Canvases[0];
    }

    public static string CreateCanvasId(string name, IEnumerable<string> existingIds)
    {
        HashSet<string> taken = new(
            (existingIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        string slug = SlugifyCanvasName(name);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "canvas";
        }

        if (!taken.Contains(slug))
        {
            return slug;
        }

        for (int suffix = 2; suffix < 10_000; suffix++)
        {
            string candidate = slug + "-" + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return slug + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    private static string SlugifyCanvasName(string name)
    {
        string raw = (name ?? "").Trim().ToLowerInvariant();
        if (raw.Length == 0)
        {
            return "";
        }

        var chars = new char[raw.Length];
        int length = 0;
        bool pendingDash = false;
        foreach (char c in raw)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                if (pendingDash && length > 0)
                {
                    chars[length++] = '-';
                }

                pendingDash = false;
                chars[length++] = c;
            }
            else if (c is ' ' or '_' or '-')
            {
                pendingDash = length > 0;
            }
        }

        return length == 0 ? "" : new string(chars, 0, length);
    }
}

