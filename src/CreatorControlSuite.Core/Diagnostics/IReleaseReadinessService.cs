namespace CreatorControlSuite.Core.Diagnostics;
public interface IReleaseReadinessService { Task<ReleaseReadinessReport> CheckAsync(CancellationToken cancellationToken=default); }
