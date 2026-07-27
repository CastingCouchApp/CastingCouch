using System.Collections.Concurrent;

namespace CreatorControlSuite.Core.Eventing;

public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _sync = new();

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            var handlers = _handlers.GetOrAdd(typeof(TEvent), static _ => []);
            handlers.Add(handler);
        }

        return new Subscription(() => Unsubscribe(handler));
    }

    public void Publish<TEvent>(TEvent eventData)
    {
        Delegate[] snapshot;
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;
            snapshot = [.. handlers];
        }

        foreach (var handler in snapshot.Cast<Action<TEvent>>())
        {
            handler(eventData);
        }
    }

    private void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;
            handlers.Remove(handler);
            if (handlers.Count == 0) _handlers.TryRemove(typeof(TEvent), out _);
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
