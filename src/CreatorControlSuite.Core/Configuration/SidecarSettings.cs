namespace CreatorControlSuite.Core.Configuration;

public sealed class SidecarSettings
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 18765;
    public string BinaryPath { get; set; } = "";
}
