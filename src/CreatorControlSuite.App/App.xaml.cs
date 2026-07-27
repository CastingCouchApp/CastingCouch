using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.App.Modules;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.Services.CreatorIntelligence;
using CreatorControlSuite.App.Themes;
using CreatorControlSuite.App.ViewModels;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.App.Views.Dialogs;
using CreatorControlSuite.App.Shell;
using CreatorControlSuite.Core.Automation;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Eventing;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Legal;
using CreatorControlSuite.Core.Licensing;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Migration;
using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Core.Profiles;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Setup;
using CreatorControlSuite.Core.Updates;
using CreatorControlSuite.Core.Validation;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.StreamDeck;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CreatorControlSuite.App;

public partial class App : Application
{
    private IHost? _host;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private ICrashReporter? _crashReporter;
    private IAppLogger? _appLogger;
    private string? _sessionMarkerPath;

    protected override async void OnStartup(StartupEventArgs e)
    {
        WriteBootstrapLog("Calling base.OnStartup");
        base.OnStartup(e);
        WriteBootstrapLog("base.OnStartup completed");

        // Fehlerhandler müssen vor dem ersten await und vor dem Aufbau des Hosts
        // registriert sein. So werden auch Fehler im sehr frühen Startvorgang erfasst.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\CreatorControlSuite.SingleInstance",
            createdNew: out bool createdNew);

            _ownsSingleInstanceMutex = createdNew;

            if (!createdNew)
            {
                // Eine zweite Ausführung öffnet nicht noch eine Suite, sondern holt
                // das bereits laufende Hauptfenster zuverlässig in den Vordergrund.
                bool activated = await TryActivateRunningInstanceAsync();
                if (!activated)
                {
                    activated = TryActivateExistingProcessWindow();
                }

                if (!activated)
                {
                    MessageBox.Show(
                        "Creator Control Suite ist bereits gestartet, konnte aber nicht in den Vordergrund geholt werden. " +
                        "Bitte prüfe den Infobereich der Windows-Taskleiste oder beende den vorhandenen Prozess im Task-Manager.",
                        "Creator Control Suite",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            string localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CreatorControlSuite");

            Directory.CreateDirectory(localAppData);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ISettingsStore>(
                        new JsonSettingsStore(
                            Path.Combine(localAppData, "settings.json")));

                    services.AddSingleton<ISecretStore>(
                        new WindowsDpapiSecretStore(
                            Path.Combine(localAppData, "Secrets")));

                    services.AddSingleton<IAppLogger>(
                        new JsonLineAppLogger(
                            Path.Combine(localAppData, "Logs")));

                    services.AddSingleton<ICrashReporter>(
                        new FileCrashReporter(
                            Path.Combine(localAppData, "CrashReports")));

                    services.AddSingleton<ISettingsValidator, SettingsValidator>();
                    services.AddSingleton<RuntimeHealthService>();

                    services.AddSingleton<IFirstRunService>(
                        provider => new FirstRunService(
                            Path.Combine(localAppData, "first-run.json"),
                            provider.GetRequiredService<ISettingsStore>()));

                    services.AddSingleton<ExternalAlertActivityService>();
                    services.AddSingleton<IIpcCommandRouter, AppIpcCommandRouter>();
                    services.AddSingleton<ILocalIpcServer, NamedPipeIpcServer>();

                    services.AddSingleton<ILegalConsentService>(new LegalConsentService(Path.Combine(localAppData, "legal-consent.json"), Path.Combine(AppContext.BaseDirectory, "Legal")));

                    services.AddSingleton<ILicenseService>(provider => new LocalLicenseService(provider.GetRequiredService<ISecretStore>(), Path.Combine(AppContext.BaseDirectory, "Keys", "license-public.pem"),
#if DEBUG
                        developmentMode: true
#else
                    developmentMode: false
#endif
                    ));

                    services.AddSingleton<IUpdateSignatureVerifier>(new RsaUpdateSignatureVerifier(Path.Combine(AppContext.BaseDirectory, "Keys", "update-public.pem")));

                    services.AddSingleton<IFeatureGate, FeatureGate>();
                    services.AddSingleton<IFeatureActionRunner, FeatureActionRunner>();
                    services.AddSingleton<IWorkflowE2eService, WorkflowE2eService>();
                    services.AddHttpClient<ILicenseServerClient, HttpLicenseServerClient>(client =>
                    {
                        client.BaseAddress = new Uri("https://license.invalid/");
                        client.Timeout = TimeSpan.FromSeconds(15);
                    });
                    services.AddSingleton<ISupportPackageService>(provider => new SupportPackageService(localAppData, provider.GetRequiredService<ISettingsStore>(), provider.GetRequiredService<IAppLogger>(), provider.GetRequiredService<RuntimeHealthService>()));
                    services.AddSingleton<IReleaseReadinessService, ReleaseReadinessService>();
                    services.AddSingleton<IInstallerSelfTestService>(provider => new InstallerSelfTestService(
                        provider.GetRequiredService<ISettingsStore>(), provider.GetRequiredService<ISettingsValidator>(),
                        provider.GetRequiredService<ILegalConsentService>(), provider.GetRequiredService<ILicenseService>(), localAppData));
                    services.AddSingleton<IBetaReadinessService, BetaReadinessService>();
                    services.AddSingleton<IStartupDependencyValidationService, StartupDependencyValidationService>();
                    services.AddSingleton<IInstallationStateService>(
                        new InstallationStateService(Path.Combine(localAppData, "installation-state.json")));


                    services.AddSingleton<IProfileService>(
                        provider => new JsonProfileService(
                            Path.Combine(localAppData, "Profiles"),
                            provider.GetRequiredService<ISettingsStore>()));

                    services.AddSingleton<ILegacyMigrationService, LegacyMigrationService>();

                    services.AddHttpClient("CreatorControlSuite.UpdateClient");

                    services.AddSingleton<IUpdateService>(provider =>
                    {
                        IHttpClientFactory httpClientFactory =
                            provider.GetRequiredService<IHttpClientFactory>();

                        HttpClient httpClient =
                            httpClientFactory.CreateClient(
                                "CreatorControlSuite.UpdateClient");

                        return new LocalUpdateService(
                            httpClient,
                            provider.GetRequiredService<ISettingsStore>(),
                            provider.GetRequiredService<IUpdateSignatureVerifier>(),
                            localAppData,
                            currentVersionProvider: GetCurrentProductVersion);
                    });

                    services.AddSingleton(
                        new StreamDeckProfileService(
                            Path.Combine(localAppData, "StreamDeck")));

                    services.AddStreamingModules();

                    services.AddSingleton<IEventBus, EventBus>();
                    services.AddSingleton<IAutomationRuleEngine, AutomationRuleEngine>();
                    services.AddSingleton<IMultiPcAgentClient, MultiPcAgentClient>();
                    services.AddSingleton<IStreamerBotClient, StreamerBotClient>();
                    services.AddSingleton<IMusicPlayerUiPresenter, MusicPlayerUiPresenter>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<CreatorIntelligenceService>();
                    services.AddSingleton<DiagnosticsPageViewModel>();
                    services.AddSingleton<IPageViewModel>(sp => sp.GetRequiredService<DiagnosticsPageViewModel>());
                    services.AddSingleton<ProfilesPageViewModel>();
                    services.AddSingleton<IPageViewModel>(sp => sp.GetRequiredService<ProfilesPageViewModel>());
                    services.AddSingleton<AboutPageViewModel>();
                    services.AddSingleton<IPageViewModel>(sp => sp.GetRequiredService<AboutPageViewModel>());
                    services.AddSingleton<MusicPlayerPageViewModel>();
                    services.AddSingleton<IPageViewModel>(sp => sp.GetRequiredService<MusicPlayerPageViewModel>());
                    services.AddSingleton<CreatorIntelligenceSectionViewModel>();
                    services.AddSingleton<PageNavigationCoordinator>();
                    services.AddSingleton<TimedAutomationTickPublisher>();
                    services.AddSingleton<AppEventBridge>();

                    services.AddSingleton<IThemeService, ThemeService>();
                    services.AddSingleton<DiagnosticService>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();
            _ = _host.Services.GetRequiredService<PageNavigationCoordinator>();
            _host.Services.GetRequiredService<TimedAutomationTickPublisher>().Start();
            _host.Services.GetRequiredService<AppEventBridge>().Start();

            try
            {
                AppSettings startupSettings = await _host.Services.GetRequiredService<ISettingsStore>().LoadAsync();
                string themeId = startupSettings.General.ThemeId;
                IFeatureGate featureGate = _host.Services.GetRequiredService<IFeatureGate>();
                ThemeDefinition theme = ThemeCatalog.Resolve(themeId);
                if (theme.IsPremium && !await featureGate.IsEnabledAsync(FeatureCatalog.PremiumThemes))
                {
                    themeId = ThemeCatalog.ClassicId;
                }

                _host.Services.GetRequiredService<IThemeService>().Apply(themeId);
            }
            catch (Exception themeException)
            {
                WriteBootstrapLog("Theme apply failed: " + themeException);
                _host.Services.GetRequiredService<IThemeService>().Apply(ThemeCatalog.ClassicId);
            }

            _crashReporter = _host.Services.GetRequiredService<ICrashReporter>();
            _appLogger = _host.Services.GetRequiredService<IAppLogger>();
            _sessionMarkerPath = Path.Combine(
                localAppData,
                "active-session.json");
            await RecoverUnexpectedPreviousTerminationAsync();
            await WriteSessionMarkerAsync();

            _appLogger.Write(
                AppLogLevel.Information,
                "Application",
                "Creator Control Suite wurde gestartet.");
            InstallationTransition installationTransition = await _host.Services.GetRequiredService<IInstallationStateService>()
                .RegisterStartAsync(GetCurrentProductVersion(), CancellationToken.None);
            _appLogger.Write(AppLogLevel.Information, "Installation",
                installationTransition.IsFirstInstall ? "Erster Programmstart erkannt." :
                installationTransition.IsUpgrade ? "Upgrade erkannt: " + installationTransition.PreviousVersion + " → " + installationTransition.CurrentVersion :
                "Normaler Programmstart.",
                properties: new Dictionary<string, string>
                {
                    ["firstInstall"] = installationTransition.IsFirstInstall.ToString(),
                    ["upgrade"] = installationTransition.IsUpgrade.ToString(),
                    ["previousVersion"] = installationTransition.PreviousVersion,
                    ["currentVersion"] = installationTransition.CurrentVersion
                });


            foreach (IStreamingModule module in _host.Services.GetServices<IStreamingModule>())
            {
                try
                {
                    await module.InitializeAsync(CancellationToken.None);
                }
                catch (Exception moduleException)
                {
                    string moduleName = module.GetType().Name;
                    WriteBootstrapLog($"Module initialization failed ({moduleName}): {moduleException}");
                    _appLogger.Write(
                        AppLogLevel.Error,
                        "Startup",
                        $"Modul {moduleName} konnte beim Start nicht initialisiert werden. Die Suite wird im eingeschränkten Modus fortgesetzt.",
                        moduleException);
                }
            }

            await _host.Services.GetRequiredService<ILocalIpcServer>()
                .StartAsync(CancellationToken.None);

            IReadOnlyList<string> dependencyValidation =
                await _host.Services
                    .GetRequiredService<IStartupDependencyValidationService>()
                    .ValidateAsync(CancellationToken.None);

            if (dependencyValidation.Count > 0)
            {
                string detail = string.Join(Environment.NewLine, dependencyValidation);
                WriteBootstrapLog("Startup dependency validation failed: " + detail);
                throw new InvalidOperationException(
                    "Startup dependency validation failed:" +
                    Environment.NewLine +
                    detail);
            }

            MainWindow = _host.Services.GetRequiredService<MainWindow>();

            ILegalConsentService legalService = _host.Services.GetRequiredService<ILegalConsentService>();
            if (await legalService.IsConsentRequiredAsync())
            {
                var legalWindow = new LegalConsentWindow(legalService);
                if (legalWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            IFirstRunService firstRunService =
                _host.Services.GetRequiredService<IFirstRunService>();

            if (await firstRunService.IsRequiredAsync())
            {
                var wizard = new FirstRunWindow(
                    _host.Services.GetRequiredService<ISettingsStore>(),
                    firstRunService,
                    _host.Services.GetRequiredService<CreatorControlSuite.Modules.Overlay.OverlayModule>());

                bool? result = wizard.ShowDialog();

                if (result != true)
                {
                    Shutdown();
                    return;
                }

                MainWindow.Show();

                if (wizard.OpenSettingsAfterCompletion &&
                    MainWindow is MainWindow mainWindow)
                {
                    mainWindow.OpenSettingsPage();
                }
            }
            else
            {
                MainWindow.Show();
            }
        }
        catch (Exception exception)
        {
            WriteBootstrapLog("Startup failed: " + exception);
            string crashReportPath = await ReportCrashAsync(exception, "Application startup");

            MessageBox.Show(
                "Creator Control Suite konnte nicht gestartet werden.\n\n" +
                exception.Message + "\n\n" +
                "Crashbericht:\n" + crashReportPath,
                "Creator Control Suite – Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }


    private static string GetCurrentProductVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            int metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static async Task<bool> TryActivateRunningInstanceAsync()
    {
        // Die erste Instanz kann sich gerade noch im Startvorgang befinden.
        // Deshalb versuchen wir die IPC-Verbindung für einige Sekunden erneut.
        for (int attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    NamedPipeIpcServer.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await pipe.ConnectAsync(timeout.Token);

                var command = new IpcCommand(
                    Guid.NewGuid().ToString("N"),
                    "activate",
                    new Dictionary<string, string>(),
                    DateTimeOffset.Now);

                using var writer = new StreamWriter(
                    pipe,
                    new UTF8Encoding(false),
                    leaveOpen: true)
                {
                    AutoFlush = true
                };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

                await writer.WriteLineAsync(JsonSerializer.Serialize(command));
                string? responseLine = await reader.ReadLineAsync(timeout.Token);
                IpcResponse? response = string.IsNullOrWhiteSpace(responseLine)
                    ? null
                    : JsonSerializer.Deserialize<IpcResponse>(responseLine);

                if (response?.Success == true)
                {
                    return true;
                }

                // Der IPC-Server kann bereits laufen, während Rechtsdialog,
                // Ersteinrichtung oder Hauptfenster noch aufgebaut werden. Eine
                // negative Antwort bedeutet deshalb nicht, dass weitere Versuche
                // aussichtslos sind. Erst nach Ablauf aller Versuche verwenden wir
                // den Prozessfenster-Fallback.
            }
            catch (OperationCanceledException)
            {
                // Erste Instanz ist eventuell noch nicht vollständig bereit.
            }
            catch (IOException)
            {
                // Pipe ist während des Startvorgangs eventuell noch nicht verfügbar.
            }

            await Task.Delay(250);
        }

        return false;
    }


    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static bool TryActivateExistingProcessWindow()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (Process process in Process.GetProcessesByName(current.ProcessName))
            {
                using (process)
                {
                    if (process.Id == current.Id)
                    {
                        continue;
                    }

                    nint handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    ShowWindow(handle, SwRestore);
                    return SetForegroundWindow(handle);
                }
            }
        }
        catch
        {
            // IPC bleibt der bevorzugte Weg; der Win32-Aufruf ist nur die Rückfallebene.
        }

        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Fehlerhandler zuerst wieder abmelden, damit Ausnahmen während des
        // kontrollierten Shutdowns nicht erneut als reguläre Laufzeitfehler
        // behandelt werden.
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        try
        {
            _appLogger?.Write(
                AppLogLevel.Information,
                "Application",
                "Creator Control Suite wird beendet.");

            if (_host is not null)
            {
                try
                {
                    using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    _host.StopAsync(shutdownTimeout.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException exception)
                {
                    WriteBootstrapLog("Host shutdown timed out.", exception);
                    _appLogger?.Write(
                        AppLogLevel.Warning,
                        "Application",
                        "Das Beenden der Hintergrunddienste hat das Zeitlimit überschritten.",
                        exception);
                }
                catch (Exception exception)
                {
                    WriteBootstrapLog("Host shutdown failed.", exception);
                    _appLogger?.Write(
                        AppLogLevel.Error,
                        "Application",
                        "Beim Beenden der Hintergrunddienste ist ein Fehler aufgetreten.",
                        exception);
                }
                finally
                {
                    try
                    {
                        _host.Dispose();
                    }
                    catch (Exception exception)
                    {
                        WriteBootstrapLog("Host dispose failed.", exception);
                    }

                    _host = null;
                }
            }
        }
        catch (Exception exception)
        {
            // Auch während des synchron abgeschlossenen Shutdowns darf keine
            // Ausnahme mehr entweichen und einen zweiten Exit-Fehler auslösen.
            WriteBootstrapLog("Application shutdown failed.", exception);
        }
        finally
        {
            DeleteSessionMarker();

            if (_ownsSingleInstanceMutex)
            {
                try
                {
                    _singleInstanceMutex?.ReleaseMutex();
                }
                catch (ApplicationException exception)
                {
                    WriteBootstrapLog("Single-instance mutex release failed.", exception);
                }
            }

            _ownsSingleInstanceMutex = false;
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            base.OnExit(e);
        }
    }

    private async void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        // Mark the exception as handled before the first await. An async event
        // handler returns to WPF at that point; leaving Handled=false until the
        // continuation runs allows WPF to terminate the process before the
        // crash report has reached disk.
        e.Handled = true;

        string path = await ReportCrashAsync(
            e.Exception,
            "WPF Dispatcher");

        MessageBox.Show(
            "Ein unerwarteter Fehler ist aufgetreten.\n\n" +
            "Crashbericht:\n" + path,
            "Creator Control Suite",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDomainUnhandledException(
        object? sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            // AppDomain is raised immediately before process termination.
            // Block briefly so the report cannot be abandoned mid-write.
            ReportCrashSynchronously(exception, "AppDomain");
        }
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        ReportCrashSynchronously(e.Exception, "TaskScheduler");
    }

    private async Task<string> ReportCrashAsync(
        Exception exception,
        string source)
    {
        try
        {
            _appLogger?.Write(
                AppLogLevel.Critical,
                source,
                exception.Message,
                exception);
        }
        catch
        {
            // A broken or locked log must not prevent the independent crash
            // report from being written.
        }

        try
        {
            ICrashReporter reporter = _crashReporter ?? CreateEmergencyCrashReporter();
            return await reporter.WriteAsync(
                exception,
                new Dictionary<string, string>
                {
                    ["source"] = source,
                    ["emergencyReporter"] = (_crashReporter is null).ToString()
                });
        }
        catch (Exception reportException)
        {
            WriteBootstrapLog(
                "Crash report could not be written.",
                reportException);
        }

        return "Crashbericht konnte nicht geschrieben werden.";
    }

    private void ReportCrashSynchronously(
        Exception exception,
        string source)
    {
        try
        {
            ReportCrashAsync(exception, source)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception reportException)
        {
            WriteBootstrapLog(
                "Synchronous crash reporting failed.",
                reportException);
        }
    }

    private static ICrashReporter CreateEmergencyCrashReporter()
    {
        string crashRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "CrashReports");

        return new FileCrashReporter(crashRoot);
    }

    private async Task RecoverUnexpectedPreviousTerminationAsync()
    {
        if (string.IsNullOrWhiteSpace(_sessionMarkerPath) ||
            !File.Exists(_sessionMarkerPath))
        {
            return;
        }

        string previousSession;
        try
        {
            previousSession = await File.ReadAllTextAsync(_sessionMarkerPath);
        }
        catch (Exception exception)
        {
            previousSession = "Sitzungsmarkierung konnte nicht gelesen werden: " +
                exception.Message;
        }

        var exceptionToReport = new InvalidOperationException(
            "Die vorherige Creator-Control-Suite-Sitzung wurde unerwartet beendet.");

        try
        {
            ICrashReporter reporter = _crashReporter ?? CreateEmergencyCrashReporter();
            await reporter.WriteAsync(
                exceptionToReport,
                new Dictionary<string, string>
                {
                    ["source"] = "Previous session recovery",
                    ["previousSession"] = previousSession
                });
        }
        catch (Exception exception)
        {
            WriteBootstrapLog(
                "Previous unexpected termination could not be reported.",
                exception);
        }
    }

    private async Task WriteSessionMarkerAsync()
    {
        if (string.IsNullOrWhiteSpace(_sessionMarkerPath))
        {
            return;
        }

        try
        {
            string marker = JsonSerializer.Serialize(
                new
                {
                    processId = Environment.ProcessId,
                    startedAt = DateTimeOffset.Now,
                    version = GetCurrentProductVersion()
                });
            await File.WriteAllTextAsync(_sessionMarkerPath, marker);
        }
        catch (Exception exception)
        {
            WriteBootstrapLog("Session marker could not be written.", exception);
        }
    }

    private void DeleteSessionMarker()
    {
        if (string.IsNullOrWhiteSpace(_sessionMarkerPath))
        {
            return;
        }

        try
        {
            if (File.Exists(_sessionMarkerPath))
            {
                File.Delete(_sessionMarkerPath);
            }
        }
        catch (Exception exception)
        {
            WriteBootstrapLog("Session marker could not be deleted.", exception);
        }
    }

    private static string GetBootstrapLogPath()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Logs");

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "startup-bootstrap.log");
    }

    private static void WriteBootstrapLog(string message, Exception? exception = null)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"));
            builder.Append(" | ");
            builder.Append(message);

            if (exception is not null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            builder.AppendLine();
            File.AppendAllText(GetBootstrapLogPath(), builder.ToString());
        }
        catch
        {
            // Bootstrap logging must never prevent application startup.
        }
    }

}
