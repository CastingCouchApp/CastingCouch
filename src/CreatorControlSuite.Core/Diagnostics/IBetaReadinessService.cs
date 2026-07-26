namespace CreatorControlSuite.Core.Diagnostics;
public interface IBetaReadinessService { Task<BetaReadinessDashboard> BuildAsync(CancellationToken cancellationToken=default); }
