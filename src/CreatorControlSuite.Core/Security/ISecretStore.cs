namespace CreatorControlSuite.Core.Security;

public interface ISecretStore
{
    Task SaveAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
