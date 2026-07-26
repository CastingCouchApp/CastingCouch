namespace CreatorControlSuite.Core.Setup;

public interface IFirstRunService
{
    Task<FirstRunState> LoadStateAsync(
        CancellationToken cancellationToken = default);

    Task SaveStateAsync(
        FirstRunState state,
        CancellationToken cancellationToken = default);

    Task<bool> IsRequiredAsync(
        CancellationToken cancellationToken = default);

    Task<FirstRunSummary> BuildSummaryAsync(
        CancellationToken cancellationToken = default);
}
