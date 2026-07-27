namespace CreatorControlSuite.Core.Licensing;

public interface IFeatureGate
{
    Task<bool> IsEnabledAsync(string feature, CancellationToken cancellationToken = default);
    Task RequireAsync(string feature, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, bool>> SnapshotAsync(CancellationToken cancellationToken = default);
}
