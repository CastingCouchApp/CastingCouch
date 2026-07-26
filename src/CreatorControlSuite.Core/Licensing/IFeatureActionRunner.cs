namespace CreatorControlSuite.Core.Licensing;
public interface IFeatureActionRunner
{
    Task RunAsync(string feature, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
