using System.Text.Json;
namespace CreatorControlSuite.Core.Setup;

public sealed class InstallationStateService(string statePath) : IInstallationStateService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path = statePath;

    public async Task<InstallationTransition> RegisterStartAsync(string currentVersion, CancellationToken ct = default)
    {
        InstallationState s = await LoadAsync(ct); bool first = string.IsNullOrWhiteSpace(s.InstalledVersion);
        bool upgrade = !first && !string.Equals(s.InstalledVersion, currentVersion, StringComparison.OrdinalIgnoreCase); string previous = s.InstalledVersion;
        if (first)
        {
            s.InstalledAt = DateTimeOffset.Now;
        }

        s.PreviousVersion = upgrade ? s.InstalledVersion : s.PreviousVersion;
        s.InstalledVersion = currentVersion; s.LastStartedAt = DateTimeOffset.Now; s.StartCount++;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); string tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(s, Options), ct); File.Move(tmp, _path, true);
        return new(first, upgrade, previous, currentVersion);
    }
    public async Task<InstallationState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
        {
            return new();
        }

        await using FileStream stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<InstallationState>(stream, Options, ct) ?? new();
    }
}
