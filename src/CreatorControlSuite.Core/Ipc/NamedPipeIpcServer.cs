using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Logging;

namespace CreatorControlSuite.Core.Ipc;

public sealed class NamedPipeIpcServer(IIpcCommandRouter router, IAppLogger logger) : ILocalIpcServer
{
    public const string PipeName = "CreatorControlSuite.CommandPipe.v1";
    private readonly IIpcCommandRouter _router = router;
    private readonly IAppLogger _logger = logger;
    private readonly ConcurrentDictionary<long, Task> _clientTasks = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _nextClientId;

    public bool IsRunning => _loop is { IsCompleted: false };
    public event EventHandler<bool>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return;
            }

            // Der Token des Aufrufers darf nur das Warten auf die Lifecycle-Sperre
            // abbrechen. Würde er mit der gesamten Serverlaufzeit verknüpft, könnte
            // ein später abgebrochener Startup- oder Host-Token die IPC-Schleife
            // unbemerkt beenden und einen veralteten Running-Zustand hinterlassen.
            _cts?.Dispose();
            _cts = null;
            _loop = null;

            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = Task.Run(() => AcceptLoopAsync(cts.Token), CancellationToken.None);
            StateChanged?.Invoke(this, true);
            _logger.Write(AppLogLevel.Information, "IPC", "Named-Pipe-Server gestartet.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            CancellationTokenSource? cts = _cts;
            Task? loop = _loop;
            if (cts is null && loop is null)
            {
                return;
            }

            cts?.Cancel();

            try
            {
                // Zuerst muss die Annahmeschleife sicher beendet sein. Andernfalls kann sie
                // nach einer vorzeitigen Momentaufnahme noch einen weiteren Client registrieren,
                // der beim Shutdown nicht mehr berücksichtigt würde.
                if (loop is not null)
                {
                    await loop.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                }

                Task[] clientTasks = [.. _clientTasks.Values];
                if (clientTasks.Length > 0)
                {
                    await Task.WhenAll(clientTasks).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Write(AppLogLevel.Warning, "IPC", "IPC-Server konnte nicht innerhalb des Zeitlimits beendet werden.");
            }
            catch (TimeoutException)
            {
                _logger.Write(AppLogLevel.Warning, "IPC", "IPC-Server konnte nicht innerhalb des Zeitlimits beendet werden.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Write(AppLogLevel.Warning, "IPC", "Fehler beim Beenden des IPC-Servers.", ex);
            }
            finally
            {
                cts?.Dispose();

                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }

                if (ReferenceEquals(_loop, loop))
                {
                    _loop = null;
                }

                // Abgeschlossene Aufgaben sofort entfernen. Noch laufende Aufgaben bleiben
                // bis zu ihrem Observer-Finally registriert und werden nicht künstlich vergessen.
                foreach (KeyValuePair<long, Task> entry in _clientTasks)
                {
                    if (entry.Value.IsCompleted)
                    {
                        _clientTasks.TryRemove(entry.Key, out _);
                    }
                }

                StateChanged?.Invoke(this, false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 5, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken);

                // Die Annahmeschleife darf nicht auf die vollständige Bearbeitung eines
                // einzelnen Clients warten. So bleiben Aktivierung, Stream Deck und
                // andere IPC-Befehle auch bei einer langsamen Gegenstelle erreichbar.
                NamedPipeServerStream clientPipe = pipe;
                pipe = null;
                long clientId = Interlocked.Increment(ref _nextClientId);
                Task clientTask = HandleClientAsync(clientPipe, cancellationToken);
                _clientTasks[clientId] = clientTask;
                _ = ObserveClientAsync(clientId, clientTask);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                _logger.Write(AppLogLevel.Error, "IPC", "IPC-Serverfehler.", ex);
            }
        }
    }

    private async Task ObserveClientAsync(long clientId, Task clientTask)
    {
        try
        {
            await clientTask;
        }
        catch (OperationCanceledException)
        {
            // Erwartet beim kontrollierten Herunterfahren.
        }
        catch (Exception ex)
        {
            _logger.Write(AppLogLevel.Error, "IPC", "Fehler bei der IPC-Clientverarbeitung.", ex);
        }
        finally
        {
            _clientTasks.TryRemove(clientId, out _);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            await HandleAsync(pipe, cancellationToken);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        // Eine verbundene Gegenstelle darf ihre eigene Anfrage nicht unbegrenzt
        // offenhalten. Der Timeout gilt nur für diese Verbindung.
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        CancellationToken requestToken = requestTimeout.Token;

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        string? line;
        try
        {
            line = await reader.ReadLineAsync(requestToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Write(AppLogLevel.Warning, "IPC", "IPC-Anfrage wegen Zeitüberschreitung verworfen.");
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        IpcResponse response;
        try
        {
            IpcCommand command = JsonSerializer.Deserialize<IpcCommand>(line)
                ?? throw new InvalidOperationException("Leerer IPC-Befehl.");
            response = await _router.ExecuteAsync(command, requestToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            response = new IpcResponse(Guid.NewGuid().ToString("N"), false,
                "Zeitüberschreitung bei der IPC-Anfrage.", new Dictionary<string, string>());
        }
        catch (Exception ex)
        {
            response = new IpcResponse(Guid.NewGuid().ToString("N"), false, ex.Message,
                new Dictionary<string, string>());
        }

        try
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
        catch (IOException)
        {
            // Der Client kann die Verbindung nach einem eigenen Timeout bereits
            // geschlossen haben. Das ist kein Fehler des dauerhaft laufenden Servers.
        }
    }
}
