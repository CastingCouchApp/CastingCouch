namespace CreatorControlSuite.Core.Configuration;

public sealed class UpdateSettings
{
    public string Channel { get; set; } = "Alpha";
    public bool AutoCheck { get; set; } = true;
    public bool BackupBeforeUpdate { get; set; } = true;
}
