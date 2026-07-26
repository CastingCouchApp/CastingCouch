namespace CreatorControlSuite.Core.Modules;

public enum ModuleHealth
{
    Unknown,
    Ready,
    Connected,
    Degraded,
    Error,
    Disabled
}

public sealed record ModuleStatus(
    string Id,
    string DisplayName,
    ModuleHealth Health,
    string Detail,
    DateTimeOffset CheckedAt);

public interface IStreamingModule
{
    string Id { get; }
    string DisplayName { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<ModuleStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public interface IConnectableModule : IStreamingModule
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}
