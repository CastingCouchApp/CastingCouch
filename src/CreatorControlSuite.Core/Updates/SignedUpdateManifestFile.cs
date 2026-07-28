using System.Text.Json;

namespace CreatorControlSuite.Core.Updates;

public static class SignedUpdateManifestFile
{
    public static async Task<SignedUpdateManifest?> LoadAdjacentAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(packagePath) ?? string.Empty;
        string manifestPath = Path.Combine(directory, "update-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<SignedUpdateManifest>(
            stream,
            cancellationToken: cancellationToken);
    }
}
