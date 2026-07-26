namespace CreatorControlSuite.Core.Migration;

public interface ILegacyMigrationService
{
    Task<IReadOnlyList<MigrationCandidate>> DetectAsync(
        CancellationToken cancellationToken = default);

    Task<MigrationResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
