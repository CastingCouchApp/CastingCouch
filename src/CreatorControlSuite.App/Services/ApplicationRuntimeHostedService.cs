using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Modules;
using Microsoft.Extensions.Hosting;

namespace CreatorControlSuite.App.Services;

public sealed class ApplicationRuntimeHostedService(
    IEnumerable<IStreamingModule> modules,
    ILocalIpcServer ipcServer,
    IAppLogger logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (IStreamingModule module in modules)
        {
            try
            {
                await module.InitializeAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.Write(
                    AppLogLevel.Error,
                    "Startup",
                    $"Modul {module.GetType().Name} konnte nicht initialisiert werden. " +
                    "Die Suite wird im eingeschränkten Modus fortgesetzt.",
                    exception);
            }
        }

        await ipcServer.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        ipcServer.StopAsync(cancellationToken);
}
