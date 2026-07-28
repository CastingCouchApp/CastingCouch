using System.Text.Json;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Profiles;

public sealed class JsonProfileService : IProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _profileRoot;
    private readonly ISettingsStore _settingsStore;

    public JsonProfileService(
        string profileRoot,
        ISettingsStore settingsStore)
    {
        _profileRoot = profileRoot;
        _settingsStore = settingsStore;
        Directory.CreateDirectory(_profileRoot);
    }

    public async Task<IReadOnlyList<ProfileSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProfileSummary>();

        foreach (string path in Directory.GetFiles(
                     _profileRoot,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                CreatorProfile profile = await ReadAsync(path, cancellationToken);

                results.Add(new ProfileSummary(
                    profile.Id,
                    profile.Name,
                    profile.Description,
                    profile.UpdatedAt));
            }
            catch
            {
            }
        }

        return [.. results.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public Task<CreatorProfile> LoadAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(GetPath(profileId), cancellationToken);
    }

    public async Task<CreatorProfile> SaveAsync(
        CreatorProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);

        profile.UpdatedAt = DateTimeOffset.UtcNow;

        string path = GetPath(profile.Id);
        string temp = path + ".tmp";

        await WriteSanitizedProfileAsync(
            temp,
            profile,
            cancellationToken);

        File.Move(temp, path, overwrite: true);

        return profile;
    }

    public async Task<CreatorProfile> CreateFromCurrentSettingsAsync(
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        var profile = new CreatorProfile
        {
            Name = name,
            Description = description,
            Settings = await _settingsStore.LoadAsync(cancellationToken)
        };

        return await SaveAsync(profile, cancellationToken);
    }

    public async Task ApplyAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        CreatorProfile profile = await LoadAsync(profileId, cancellationToken);
        AppSettings current = await _settingsStore.LoadAsync(cancellationToken);
        profile.Settings.StreamerBot.Password =
            current.StreamerBot.Password;
        await _settingsStore.SaveAsync(profile.Settings, cancellationToken);
    }

    public Task DeleteAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(profileId);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<string> ExportAsync(
        string profileId,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        CreatorProfile profile = await LoadAsync(profileId, cancellationToken);
        string? targetDirectory = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        await WriteSanitizedProfileAsync(
            targetPath,
            profile,
            cancellationToken);

        return targetPath;
    }

    public async Task<CreatorProfile> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        CreatorProfile profile = await ReadAsync(sourcePath, cancellationToken);
        profile.Settings.StreamerBot.Password = "";
        profile.Id = Guid.NewGuid().ToString("N");
        profile.Name += " (Import)";
        profile.CreatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        return await SaveAsync(profile, cancellationToken);
    }

    private async Task<CreatorProfile> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Profil wurde nicht gefunden.",
                path);
        }

        await using FileStream stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<CreatorProfile>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException(
                   "Profil konnte nicht gelesen werden.");
    }

    private string GetPath(string profileId)
    {
        string safeId = string.Concat(
            profileId.Where(character =>
                char.IsLetterOrDigit(character) ||
                character is '-' or '_'));

        return Path.Combine(_profileRoot, safeId + ".json");
    }

    private static async Task WriteSanitizedProfileAsync(
        string path,
        CreatorProfile profile,
        CancellationToken cancellationToken)
    {
        string password = profile.Settings.StreamerBot.Password;
        try
        {
            profile.Settings.StreamerBot.Password = "";
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(profile, JsonOptions),
                cancellationToken);
        }
        finally
        {
            profile.Settings.StreamerBot.Password = password;
        }
    }
}
