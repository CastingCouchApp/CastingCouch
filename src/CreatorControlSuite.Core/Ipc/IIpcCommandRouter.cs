namespace CreatorControlSuite.Core.Ipc;

public interface IIpcCommandRouter
{
    Task<IpcResponse> ExecuteAsync(
        IpcCommand command,
        CancellationToken cancellationToken = default);
}
