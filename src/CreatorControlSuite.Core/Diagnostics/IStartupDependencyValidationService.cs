namespace CreatorControlSuite.Core.Diagnostics;

public interface IStartupDependencyValidationService
{
    Task<IReadOnlyList<string>> ValidateAsync(CancellationToken cancellationToken = default);
}
