namespace CreatorControlSuite.Modules.Overlay.Assets;

public sealed class OverlayAssetInfo
{
    public string Id { get; init; } = "";
    public string FileName { get; init; } = "";
    public string OriginalName { get; init; } = "";
    public string ContentType { get; init; } = "application/octet-stream";
    public long SizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string LocalPath { get; init; } = "";
    public string PublicUrl { get; init; } = "";
}

public sealed class OverlayAssetValidationException : Exception
{
    public OverlayAssetValidationException(string message) : base(message)
    {
    }
}
