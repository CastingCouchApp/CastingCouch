namespace CreatorControlSuite.Core.Diagnostics;
public interface ISupportPackageService { Task<SupportPackageResult> CreateAsync(string targetPath,SupportPackageOptions options,CancellationToken cancellationToken=default); }
