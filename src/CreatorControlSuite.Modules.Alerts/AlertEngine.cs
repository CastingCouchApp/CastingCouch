using System.Threading.Channels;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.Modules.Alerts;

public sealed class AlertEngine : IAlertEngine
{
    private readonly ISettingsStore _settingsStore;
    private readonly AlertDefinitionProvider _definitions;
    private readonly ObsAlertRenderer _renderer;

    private readonly object _stateLock = new();
    private Channel<AlertRequest>? _channel;
    private CancellationTokenSource? _workerCancellation;
    private Task? _worker;
    private AlertPlaybackState _state =
        new(false, null, 0, null, "Gestoppt");
    private int _queueLength;

    public AlertEngine(
        ISettingsStore settingsStore,
        AlertDefinitionProvider definitions,
        ObsAlertRenderer renderer)
    {
        _settingsStore = settingsStore;
        _definitions = definitions;
        _renderer = renderer;
    }

    public event EventHandler<AlertPlaybackState>? StateChanged;

    public AlertPlaybackState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        if (_worker is not null)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        _channel = Channel.CreateBounded<AlertRequest>(
            new BoundedChannelOptions(
                Math.Max(1, settings.Alerts.QueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        _workerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        _worker = Task.Run(
            () => WorkerAsync(
                _workerCancellation.Token),
            CancellationToken.None);

        UpdateState(
            new AlertPlaybackState(
                false,
                null,
                0,
                null,
                "Bereit"));
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        _workerCancellation?.Cancel();

        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch
            {
            }
        }

        await _renderer.HideAsync(cancellationToken);

        _workerCancellation?.Dispose();
        _workerCancellation = null;
        _worker = null;
        _channel = null;
        Interlocked.Exchange(ref _queueLength, 0);

        UpdateState(
            new AlertPlaybackState(
                false,
                null,
                0,
                null,
                "Gestoppt"));
    }

    public async Task EnqueueAsync(
        AlertRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            await StartAsync(cancellationToken);
        }

        var writer = _channel!.Writer;

        await writer.WriteAsync(
            request,
            cancellationToken);

        var queueLength = Interlocked.Increment(
            ref _queueLength);

        UpdateState(
            State with
            {
                QueueLength = queueLength,
                Detail = "Alert eingereiht"
            });
    }

    public Task ClearQueueAsync(
        CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            return Task.CompletedTask;
        }

        while (_channel.Reader.TryRead(out _))
        {
            Interlocked.Decrement(ref _queueLength);
        }

        UpdateState(
            State with
            {
                QueueLength = 0,
                Detail = "Warteschlange geleert"
            });

        return Task.CompletedTask;
    }

    public async Task StopCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        await _renderer.HideAsync(cancellationToken);

        UpdateState(
            new AlertPlaybackState(
                false,
                null,
                Math.Max(0, Volatile.Read(ref _queueLength)),
                null,
                "Aktueller Alert gestoppt"));
    }

    public async Task<AlertPreview> BuildPreviewAsync(
        string type,
        string user,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken cancellationToken = default)
    {
        var definition = await _definitions.GetAsync(
            type,
            cancellationToken);

        var rendered = AlertTemplateRenderer.Render(
            definition.TextTemplate,
            user,
            variables ??
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase));

        return new AlertPreview(
            definition.Type,
            rendered,
            definition.MediaPath,
            definition.SoundPath,
            definition.Duration,
            definition.Animation,
            definition.FontFace,
            definition.FontSize,
            definition.FontColor);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task WorkerAsync(
        CancellationToken cancellationToken)
    {
        var channel = _channel
                      ?? throw new InvalidOperationException(
                          "Alert-Queue wurde nicht initialisiert.");

        try
        {
            while (await channel.Reader.WaitToReadAsync(
                       cancellationToken))
            {
                while (channel.Reader.TryRead(out var request))
                {
                    Interlocked.Decrement(ref _queueLength);

                    await PlayOneAsync(
                        request,
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                await _renderer.HideAsync(
                    CancellationToken.None);
            }
            catch
            {
            }
        }
    }

    private async Task PlayOneAsync(
        AlertRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        var definition = await _definitions.GetAsync(
            request.Type,
            cancellationToken);

        if (!definition.Enabled)
        {
            return;
        }

        var text = AlertTemplateRenderer.Render(
            definition.TextTemplate,
            request.User,
            request.Variables);

        UpdateState(
            new AlertPlaybackState(
                true,
                request,
                Math.Max(0, Volatile.Read(ref _queueLength)),
                DateTimeOffset.Now,
                "Alert läuft"));

        try
        {
            if (settings.Alerts.StopPreviousMediaBeforeNext)
            {
                await _renderer.HideAsync(
                    cancellationToken);
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    Math.Max(
                        0,
                        settings.Alerts.InterAlertDelayMilliseconds)),
                cancellationToken);

            await _renderer.ShowAsync(
                definition,
                text,
                cancellationToken);

            await Task.Delay(
                definition.Duration,
                cancellationToken);
        }
        finally
        {
            await _renderer.HideAsync(
                CancellationToken.None);

            UpdateState(
                new AlertPlaybackState(
                    false,
                    null,
                    Math.Max(0, Volatile.Read(ref _queueLength)),
                    null,
                    "Bereit"));
        }
    }

    private void UpdateState(
        AlertPlaybackState state)
    {
        lock (_stateLock)
        {
            _state = state;
        }

        StateChanged?.Invoke(
            this,
            state);
    }
}
