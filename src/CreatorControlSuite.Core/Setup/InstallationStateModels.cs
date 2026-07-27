namespace CreatorControlSuite.Core.Setup;

public sealed class InstallationState
{
    public string InstalledVersion { get; set; } = "";
    public DateTimeOffset InstalledAt { get; set; }
    public DateTimeOffset LastStartedAt { get; set; }
    public string PreviousVersion { get; set; } = "";
    public int StartCount { get; set; }
}
public sealed record InstallationTransition(bool IsFirstInstall, bool IsUpgrade, string PreviousVersion, string CurrentVersion);
