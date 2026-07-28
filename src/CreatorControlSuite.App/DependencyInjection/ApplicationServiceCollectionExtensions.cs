using System.Net.Http;
using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.App.Modules;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.Services.CreatorIntelligence;
using CreatorControlSuite.App.Shell;
using CreatorControlSuite.App.Themes;
using CreatorControlSuite.App.ViewModels;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Automation;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Eventing;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Legal;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Migration;
using CreatorControlSuite.Core.Profiles;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Setup;
using CreatorControlSuite.Core.Updates;
using CreatorControlSuite.Core.Validation;
using CreatorControlSuite.Modules.StreamDeck;
using CreatorControlSuite.Modules.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CreatorControlSuite.App.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddCreatorControlSuiteApplication(
        this IServiceCollection services,
        string localAppData,
        Func<string> currentVersionProvider)
    {
        services.AddSingleton<ISecretStore>(
            new WindowsDpapiSecretStore(Path.Combine(localAppData, "Secrets")));
        services.AddSingleton(
            new JsonSettingsStore(Path.Combine(localAppData, "settings.json")));
        services.AddSingleton<ISettingsStore>(provider =>
            new SecretProtectedSettingsStore(
                provider.GetRequiredService<JsonSettingsStore>(),
                provider.GetRequiredService<ISecretStore>()));
        services.AddSingleton<IAppLogger>(
            new JsonLineAppLogger(Path.Combine(localAppData, "Logs")));
        services.AddSingleton<ICrashReporter>(
            new FileCrashReporter(Path.Combine(localAppData, "CrashReports")));
        services.AddSingleton<ISettingsValidator, SettingsValidator>();
        services.AddSingleton<SettingsApplicationService>();
        services.AddSingleton<RuntimeHealthService>();
        services.AddSingleton<IFirstRunService>(provider =>
            new FirstRunService(
                Path.Combine(localAppData, "first-run.json"),
                provider.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<ExternalAlertActivityService>();
        services.AddSingleton<IIpcCommandRouter, AppIpcCommandRouter>();
        services.AddSingleton<ILocalIpcServer, NamedPipeIpcServer>();
        services.AddSingleton<ILegalConsentService>(
            new LegalConsentService(
                Path.Combine(localAppData, "legal-consent.json"),
                Path.Combine(AppContext.BaseDirectory, "Legal")));
        services.AddSingleton<ILegalDocumentLauncher, LegalDocumentLauncher>();
        services.AddSingleton<IUpdateSignatureVerifier>(
            new RsaUpdateSignatureVerifier(
                Path.Combine(AppContext.BaseDirectory, "Keys", "update-public.pem")));
        services.AddSingleton<IWorkflowE2eService, WorkflowE2eService>();
        services.AddSingleton<ISupportPackageService>(provider =>
            new SupportPackageService(
                localAppData,
                provider.GetRequiredService<ISettingsStore>(),
                provider.GetRequiredService<IAppLogger>(),
                provider.GetRequiredService<RuntimeHealthService>()));
        services.AddSingleton<IReleaseReadinessService, ReleaseReadinessService>();
        services.AddSingleton<IInstallerSelfTestService>(provider =>
            new InstallerSelfTestService(
                provider.GetRequiredService<ISettingsStore>(),
                provider.GetRequiredService<ISettingsValidator>(),
                provider.GetRequiredService<ILegalConsentService>(),
                localAppData));
        services.AddSingleton<IBetaReadinessService, BetaReadinessService>();
        services.AddSingleton<IStartupDependencyValidationService, StartupDependencyValidationService>();
        services.AddSingleton<IInstallationStateService>(
            new InstallationStateService(
                Path.Combine(localAppData, "installation-state.json")));
        services.AddSingleton<IProfileService>(provider =>
            new JsonProfileService(
                Path.Combine(localAppData, "Profiles"),
                provider.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<ILegacyMigrationService, LegacyMigrationService>();
        services.AddHttpClient("CreatorControlSuite.UpdateClient");
        services.AddSingleton<IUpdateService>(provider =>
            new LocalUpdateService(
                provider.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("CreatorControlSuite.UpdateClient"),
                provider.GetRequiredService<ISettingsStore>(),
                provider.GetRequiredService<IUpdateSignatureVerifier>(),
                localAppData,
                currentVersionProvider: currentVersionProvider));
        services.AddSingleton<UpdateWorkflowService>();
        services.AddSingleton(
            new StreamDeckProfileService(Path.Combine(localAppData, "StreamDeck")));
        services.AddStreamingModules();
        services.AddSingleton<IWorkflowObsCapability, WorkflowObsCapability>();
        services.AddSingleton<IWorkflowMusicCapability, WorkflowMusicCapability>();
        services.AddSingleton<IWorkflowAlertCapability, WorkflowAlertCapability>();
        services.AddSingleton<IWorkflowOverlayCapability, WorkflowOverlayCapability>();
        services.AddSingleton<
            IOverlayCanvasApplicationService,
            OverlayCanvasApplicationService>();
        services.AddSingleton<
            IAlertDefinitionApplicationService,
            AlertDefinitionApplicationService>();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IAutomationRuleEngine, AutomationRuleEngine>();
        services.AddSingleton<IMultiPcAgentClient, MultiPcAgentClient>();
        services.AddSingleton<IMultiPcPairingClient, MultiPcPairingClient>();
        services.AddSingleton<IRemoteUpdateTransport, RemoteUpdateTransport>();
        services.AddSingleton<RemoteUpdateRolloutService>();
        services.AddSingleton<IStreamerBotClient, StreamerBotClient>();
        services.AddSingleton<IMusicPlayerUiPresenter, MusicPlayerUiPresenter>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<CreatorIntelligenceService>();
        services.AddSingleton<DiagnosticsPageViewModel>();
        services.AddSingleton<IPageViewModel>(
            provider => provider.GetRequiredService<DiagnosticsPageViewModel>());
        services.AddSingleton<ProfilesPageViewModel>();
        services.AddSingleton<IPageViewModel>(
            provider => provider.GetRequiredService<ProfilesPageViewModel>());
        services.AddSingleton<AboutPageViewModel>();
        services.AddSingleton<IPageViewModel>(
            provider => provider.GetRequiredService<AboutPageViewModel>());
        services.AddSingleton<MusicPlayerPageViewModel>();
        services.AddSingleton<IPageViewModel>(
            provider => provider.GetRequiredService<MusicPlayerPageViewModel>());
        services.AddSingleton(provider =>
            new UpdatePageViewModel(
                provider.GetRequiredService<UpdateWorkflowService>(),
                provider.GetRequiredService<IUpdateService>(),
                currentVersionProvider));
        services.AddSingleton<MigrationPageViewModel>();
        services.AddSingleton<LegalPageViewModel>();
        services.AddSingleton<GeneralSettingsPageViewModel>();
        services.AddSingleton<TwitchGoalsPageViewModel>();
        services.AddSingleton<SpotifyAutomationPageViewModel>();
        services.AddSingleton<WorkflowSessionPageViewModel>();
        services.AddSingleton<OverlayConnectionSettingsPageViewModel>();
        services.AddSingleton<OverlayCanvasPageViewModel>();
        services.AddSingleton<OverlayExtensionPacksPageViewModel>();
        services.AddSingleton<AlertLibraryPageViewModel>();
        services.AddSingleton<AlertDefinitionEditorViewModel>();
        services.AddSingleton<AlertRuntimePageViewModel>();
        services.AddSingleton<CreatorIntelligenceSectionViewModel>();
        services.AddSingleton<PageNavigationCoordinator>();
        services.AddSingleton<TimedAutomationTickPublisher>();
        services.AddSingleton<AppEventBridge>();
        services.AddSingleton<IHostedService>(
            provider => provider.GetRequiredService<TimedAutomationTickPublisher>());
        services.AddSingleton<IHostedService>(
            provider => provider.GetRequiredService<AppEventBridge>());
        services.AddHostedService<ApplicationRuntimeHostedService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IThemeSelectionService>(
            provider => provider.GetRequiredService<IThemeService>());
        services.AddSingleton<DiagnosticService>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
