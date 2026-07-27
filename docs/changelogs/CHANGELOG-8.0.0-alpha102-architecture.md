# Changelog 8.0.0-alpha102 – Architektur-Review-Umsetzung

## Tests

- Substanztents für `StreamWorkflowService`, `AlertEngine`, `LocalLicenseService`/`FeatureGate`
- Table-driven `SettingsValidator`-Coverage für alle Error-Codes
- Tests für `AutomationRuleEngine`, `SecretJsonStore`, `EventBus`

## Erweiterbarkeit / Wartbarkeit

- `IAlertRenderer` für mockbare Alert-Wiedergabe
- `IModuleRegistration` + `AddStreamingModules()` statt Inline-DI
- Extrahierte Services: `MultiPcAgentClient`, `StreamerBotClient`, `MusicPlayerUiPresenter`
- MVVM-Fundament (`NavigationService`, `DiagnosticsPageViewModel`)
- `SecretJsonStore<T>`, `AutomationRuleEngine`, `NowPlayingWidget`
- Agent: `WithObsControl`-Helper für OBS-Routen
- `IEventBus` nach Core verschoben und über `AppEventBridge` verdrahtet
