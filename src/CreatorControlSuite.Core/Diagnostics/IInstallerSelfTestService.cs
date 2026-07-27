namespace CreatorControlSuite.Core.Diagnostics;

public interface IInstallerSelfTestService { Task<InstallerSelfTestReport> RunAsync(CancellationToken cancellationToken = default); }
