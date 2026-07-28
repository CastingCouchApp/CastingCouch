using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class ChatHistoryService(
    IChatHistoryStore store,
    IOverlayRealtimeHub hub,
    IOverlayLayoutStore layoutStore,
    ISettingsStore settingsStore) : IChatHistoryService
{
    private readonly object _timerGate = new();
    private CancellationTokenSource? _debounceCts;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    public async Task<int> ResolveCapacityAsync(CancellationToken cancellationToken = default)
    {
        int maxLines = 80;
        try
        {
            foreach (string instanceId in layoutStore.ListInstanceIds())
            {
                OverlayLayout layout = await layoutStore.LoadAsync(instanceId, cancellationToken);
                foreach (OverlayLayoutItem item in layout.Items)
                {
                    if (!string.Equals(item.Type, "chat", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (item.Props.TryGetValue("maxLines", out JsonElement value))
                    {
                        int lines = value.ValueKind switch
                        {
                            JsonValueKind.Number => value.TryGetInt32(out int n) ? n : 80,
                            JsonValueKind.String => int.TryParse(value.GetString(), out int parsed) ? parsed : 80,
                            _ => 80
                        };
                        maxLines = Math.Max(maxLines, lines);
                    }
                }
            }
        }
        catch
        {
            // Fallback bleibt 80.
        }

        return Math.Clamp(maxLines * 2, 0, 2000);
    }

    public async Task SyncCapacityToHubAsync(CancellationToken cancellationToken = default)
    {
        int capacity = await ResolveCapacityAsync(cancellationToken);
        hub.ConfigureChatBuffer(capacity);
        try
        {
            AppSettings settings = await settingsStore.LoadAsync(cancellationToken);
            settings.Overlay.Chat.MaxBufferedMessages = capacity;
        }
        catch
        {
            // Kapazität am Hub reicht; Settings-Sync ist best-effort.
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await SyncCapacityToHubAsync(cancellationToken);
        IReadOnlyList<OverlayRealtimeEvent> events = await store.LoadAsync(cancellationToken);
        hub.RestoreBufferedChatEvents(events);
        hub.ChatBufferChanged -= OnChatBufferChanged;
        hub.ChatBufferChanged += OnChatBufferChanged;
    }

    public void ScheduleSave()
    {
        lock (_timerGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceDelay, token);
                    await FlushAsync(token);
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OverlayRealtimeEvent> events = hub.GetBufferedChatEvents();
        await store.SaveAsync(events, cancellationToken);
    }

    public async Task ClearAndBroadcastAsync(CancellationToken cancellationToken = default)
    {
        hub.ClearBufferedChat();
        await store.SaveAsync([], cancellationToken);
        await hub.PublishEventAsync(OverlayEventBridge.AppChatClear(), cancellationToken);
    }

    public void RemoveMessage(string messageId)
    {
        hub.RemoveBufferedChatMessage(messageId);
        ScheduleSave();
    }

    public void RemoveUserMessages(string userLogin, string userId)
    {
        hub.RemoveBufferedChatMessagesByUser(userLogin, userId);
        ScheduleSave();
    }

    private void OnChatBufferChanged() => ScheduleSave();
}
