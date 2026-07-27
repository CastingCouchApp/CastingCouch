namespace CreatorControlSuite.Core.Eventing;

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
    void Publish<TEvent>(TEvent eventData);
}
