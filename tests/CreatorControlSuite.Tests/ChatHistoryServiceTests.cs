using System.Collections.Concurrent;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Tests;

public sealed class ChatHistoryServiceTests
{
    [Fact]
    public async Task ResolveCapacityAsync_UsesMaxLinesFromChatWidget()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ccs-chat-svc-{Guid.NewGuid():N}");
        string layouts = Path.Combine(root, "layouts");
        Directory.CreateDirectory(layouts);

        try
        {
            OverlayLayout layout = OverlayLayout.CreateDefault();
            layout.Items =
            [
                new OverlayLayoutItem
                {
                    Id = "chat1",
                    Type = "chat",
                    Props = new Dictionary<string, JsonElement>
                    {
                        ["maxLines"] = JsonSerializer.SerializeToElement(120)
                    }
                }
            ];
            await File.WriteAllTextAsync(
                Path.Combine(layouts, "main.json"),
                JsonSerializer.Serialize(layout, OverlayLayoutStore.JsonOptions));

            var settings = new AppSettings();
            settings.Overlay.RootPath = root;
            var service = CreateService(settings, layouts, Path.Combine(root, "chat-history.json"));

            int capacity = await service.ResolveCapacityAsync();

            Assert.Equal(240, capacity);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InitializeAsync_DoesNotDeadlockOnSingleThreadedSyncContext()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ccs-chat-deadlock-{Guid.NewGuid():N}");
        string layouts = Path.Combine(root, "layouts");
        Directory.CreateDirectory(layouts);

        try
        {
            OverlayLayout layout = OverlayLayout.CreateDefault();
            layout.Items =
            [
                new OverlayLayoutItem
                {
                    Id = "chat1",
                    Type = "chat",
                    Props = new Dictionary<string, JsonElement>
                    {
                        ["maxLines"] = JsonSerializer.SerializeToElement(40)
                    }
                }
            ];
            await File.WriteAllTextAsync(
                Path.Combine(layouts, "main.json"),
                JsonSerializer.Serialize(layout, OverlayLayoutStore.JsonOptions));

            var settings = new AppSettings();
            settings.Overlay.RootPath = root;
            var service = CreateService(settings, layouts, Path.Combine(root, "chat-history.json"));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RunOnSingleThreadedSynchronizationContextAsync(
                () => service.InitializeAsync(timeout.Token),
                timeout.Token);

            // Basis ist 80 Zeilen; 40 im Layout senkt die Kapazität nicht.
            Assert.Equal(160, await service.ResolveCapacityAsync());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static ChatHistoryService CreateService(
        AppSettings settings,
        string layoutsRoot,
        string historyPath)
    {
        var settingsStore = new MemorySettingsStore(settings);
        var layoutStore = new OverlayLayoutStore(layoutsRoot);
        var hub = new OverlayRealtimeHub();
        var historyStore = new ChatHistoryStore(historyPath);
        return new ChatHistoryService(historyStore, hub, layoutStore, settingsStore);
    }

    private static async Task RunOnSingleThreadedSynchronizationContextAsync(
        Func<Task> work,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var context = new SingleThreadedSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Task task = work();
                context.Run(task, cancellationToken);
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        })
        {
            IsBackground = true,
            Name = "ChatHistoryService-SyncContext"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(cancellationToken);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // temp cleanup is best-effort
        }
    }

    private sealed class MemorySettingsStore(AppSettings settings) : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Mimics the WPF Dispatcher SynchronizationContext: async continuations are posted
    /// back to a single thread that must pump the queue. Blocking that thread with
    /// GetResult() while an await needs to resume here deadlocks.
    /// </summary>
    private sealed class SingleThreadedSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private int _operationCount;

        public override void Post(SendOrPostCallback d, object? state) =>
            _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) =>
            d(state);

        public override void OperationStarted() => Interlocked.Increment(ref _operationCount);

        public override void OperationCompleted() => Interlocked.Decrement(ref _operationCount);

        public void Run(Task task, CancellationToken cancellationToken)
        {
            while (!task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_queue.TryTake(out (SendOrPostCallback Callback, object? State) item, 50))
                {
                    item.Callback(item.State);
                }
            }

            task.GetAwaiter().GetResult();
        }
    }
}
