namespace CreatorControlSuite.Core.Profiles;

public interface IProfileService
{
    Task<IReadOnlyList<ProfileSummary>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<CreatorProfile> LoadAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<CreatorProfile> SaveAsync(
        CreatorProfile profile,
        CancellationToken cancellationToken = default);

    Task<CreatorProfile> CreateFromCurrentSettingsAsync(
        string name,
        string description,
        CancellationToken cancellationToken = default);

    Task ApplyAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<string> ExportAsync(
        string profileId,
        string targetPath,
        CancellationToken cancellationToken = default);

    Task<CreatorProfile> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
