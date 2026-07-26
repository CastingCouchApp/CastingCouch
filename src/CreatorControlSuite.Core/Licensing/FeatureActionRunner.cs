namespace CreatorControlSuite.Core.Licensing;
public sealed class FeatureActionRunner : IFeatureActionRunner
{
    private readonly IFeatureGate _featureGate;
    public FeatureActionRunner(IFeatureGate featureGate) => _featureGate = featureGate;
    public async Task RunAsync(string feature, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await _featureGate.RequireAsync(feature, cancellationToken);
        await action(cancellationToken);
    }
}
