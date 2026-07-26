using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.Modules.Alerts;

public sealed class AlertsModule : IConnectableModule
{
    private readonly IAlertEngine _engine;

    public AlertsModule(IAlertEngine engine)
    {
        _engine = engine;
    }

    public string Id => "alerts";
    public string DisplayName => "Alerts";

    public event EventHandler<AlertPlaybackState>? StateChanged
    {
        add => _engine.StateChanged += value;
        remove => _engine.StateChanged -= value;
    }

    public AlertPlaybackState State => _engine.State;

    public Task InitializeAsync(
        CancellationToken cancellationToken)
    {
        return _engine.StartAsync(cancellationToken);
    }

    public Task ConnectAsync(
        CancellationToken cancellationToken)
    {
        return _engine.StartAsync(cancellationToken);
    }

    public Task DisconnectAsync(
        CancellationToken cancellationToken)
    {
        return _engine.StopAsync(cancellationToken);
    }

    public Task EnqueueAsync(
        string type,
        string user,
        IReadOnlyDictionary<string, string>? variables = null,
        int priority = 100,
        CancellationToken cancellationToken = default)
    {
        return _engine.EnqueueAsync(
            new AlertRequest(
                Guid.NewGuid(),
                type,
                user,
                variables ??
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.Now,
                priority),
            cancellationToken);
    }

    public Task<AlertPreview> BuildPreviewAsync(
        string type,
        string user,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        return _engine.BuildPreviewAsync(
            type,
            user,
            variables,
            cancellationToken);
    }

    public Task StopCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        return _engine.StopCurrentAsync(cancellationToken);
    }

    public Task ClearQueueAsync(
        CancellationToken cancellationToken = default)
    {
        return _engine.ClearQueueAsync(cancellationToken);
    }

    public Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var state = _engine.State;

        return Task.FromResult(
            new ModuleStatus(
                Id,
                DisplayName,
                state.IsRunning
                    ? ModuleHealth.Connected
                    : ModuleHealth.Ready,
                state.IsRunning
                    ? $"{state.Current?.Type} · Queue: {state.QueueLength}"
                    : $"Bereit · Queue: {state.QueueLength}",
                DateTimeOffset.Now));
    }
}
