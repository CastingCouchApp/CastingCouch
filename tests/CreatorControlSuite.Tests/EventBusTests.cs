using CreatorControlSuite.Core.Eventing;

namespace CreatorControlSuite.Tests;

public sealed class EventBusTests
{
    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        var bus = new EventBus();
        string? received = null;

        using IDisposable subscription = bus.Subscribe<string>(evt => received = evt);

        bus.Publish("obs-connected");

        Assert.Equal("obs-connected", received);
    }

    [Fact]
    public void Dispose_UnsubscribesHandler()
    {
        var bus = new EventBus();
        int count = 0;
        IDisposable subscription = bus.Subscribe<string>(_ => count++);

        subscription.Dispose();
        bus.Publish("ignored");

        Assert.Equal(0, count);
    }
}
