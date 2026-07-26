using System.Globalization;
using System.Text;

namespace CreatorControlSuite.Core.Updates;

/// <summary>
/// Kanonische Signatur-Payload für Update-Manifeste.
/// Muss 1:1 mit build/New-UpdateArtifacts.ps1 übereinstimmen.
/// </summary>
public static class UpdateManifestCanonical
{
    public const string ProductId = "CreatorControlSuite";

    public static string GetPayload(SignedUpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var builder = new StringBuilder();
        builder.Append(manifest.ProductId).Append('\n');
        builder.Append(manifest.Version).Append('\n');
        builder.Append(manifest.Channel).Append('\n');
        builder.Append(manifest.PackageFileName).Append('\n');
        builder.Append(manifest.PackageSha256).Append('\n');
        builder.Append(manifest.PackageSizeBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(manifest.PublishedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(manifest.MinimumVersion).Append('\n');
        builder.Append(manifest.ReleaseNotes.Replace("\r\n", "\n", StringComparison.Ordinal));
        return builder.ToString();
    }
}
