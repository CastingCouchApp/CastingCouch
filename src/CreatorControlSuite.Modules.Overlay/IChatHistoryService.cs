namespace CreatorControlSuite.Modules.Overlay;

public interface IChatHistoryService
{
    int ResolveCapacity();

    void SyncCapacityToHub();

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void ScheduleSave();

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task ClearAndBroadcastAsync(CancellationToken cancellationToken = default);

    void RemoveMessage(string messageId);

    void RemoveUserMessages(string userLogin, string userId);
}
