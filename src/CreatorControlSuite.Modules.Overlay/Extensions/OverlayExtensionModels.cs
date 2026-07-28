namespace CreatorControlSuite.Modules.Overlay.Extensions;

/// <summary>
/// Manifest-Schema (apiVersion 1) eines Overlay-Extension-Packs.
/// Wird 1:1 aus <c>manifest.json</c> im Wurzelverzeichnis des ZIP-Pakets gelesen.
/// </summary>
public sealed class OverlayExtensionManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public int ApiVersion { get; set; }
    public List<OverlayExtensionWidget> Widgets { get; set; } = [];
    public List<OverlayExtensionEffect> Effects { get; set; } = [];
    public List<OverlayExtensionAnimation> Animations { get; set; } = [];
    public List<OverlayExtensionFont> Fonts { get; set; } = [];
    public List<string> Assets { get; set; } = [];
}

public sealed class OverlayExtensionWidget
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Entry { get; set; } = "";
    public string? Css { get; set; }
}

public sealed class OverlayExtensionEffect
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Entry { get; set; } = "";
    public string? Css { get; set; }
}

public sealed class OverlayExtensionAnimation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Entry { get; set; } = "";
    public string? Css { get; set; }
}

public sealed class OverlayExtensionFont
{
    public string Family { get; set; } = "";
    public string Src { get; set; } = "";
    public string? Weight { get; set; }
    public string? Style { get; set; }
}

/// <summary>
/// Katalog-Eintrag für ein installiertes Extension-Pack (Rückgabe von
/// <see cref="IOverlayExtensionStore.ListCatalog"/> / <see cref="IOverlayExtensionStore.InstallFromZipAsync"/>).
/// </summary>
public sealed class OverlayExtensionPackSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public int ApiVersion { get; set; }
    public IReadOnlyList<OverlayExtensionWidget> Widgets { get; set; } = [];
    public IReadOnlyList<OverlayExtensionEffect> Effects { get; set; } = [];
    public IReadOnlyList<OverlayExtensionAnimation> Animations { get; set; } = [];
    public IReadOnlyList<OverlayExtensionFont> Fonts { get; set; } = [];
    public IReadOnlyList<string> Assets { get; set; } = [];
}

/// <summary>
/// Wird für alle Ablehnungsgründe beim Installieren eines Extension-Packs geworfen
/// (fehlendes/ungültiges Manifest, Zip-Slip, nicht erlaubter Dateityp, zu großes Archiv, …).
/// </summary>
public sealed class OverlayExtensionValidationException(string message) : Exception(message);
