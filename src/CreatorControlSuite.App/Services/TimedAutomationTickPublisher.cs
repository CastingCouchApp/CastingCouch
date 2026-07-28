using System.Windows.Threading;
using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.Core.Eventing;
using Microsoft.Extensions.Hosting;

namespace CreatorControlSuite.App.Services;

/// <summary>
/// Publishes periodic timed-automation ticks on the UI dispatcher via <see cref="IEventBus"/>.
/// </summary>
public sealed class TimedAutomationTickPublisher : IHostedService, IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public TimedAutomationTickPublisher(IEventBus eventBus)
    {
        _eventBus = eventBus;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => _eventBus.Publish(new TimedAutomationTick(DateTimeOffset.UtcNow));
    }

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        Start();
        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }
}
