namespace CreatorControlSuite.Core.Licensing;

public sealed class FeatureActionRunner(IFeatureGate featureGate) : IFeatureActionRunner
{
    private readonly IFeatureGate _featureGate = featureGate;

    public async Task RunAsync(string feature, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await _featureGate.RequireAsync(feature, cancellationToken);
        await action(cancellationToken);
    }
}
