namespace CreatorControlSuite.Core.Setup;

public interface IInstallationStateService
{
    Task<InstallationTransition> RegisterStartAsync(string currentVersion, CancellationToken cancellationToken = default);
    Task<InstallationState> LoadAsync(CancellationToken cancellationToken = default);
}
