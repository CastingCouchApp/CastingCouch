# Creator Control Suite 3.0 architecture migration

This alpha starts the migration without removing existing functionality.

## Completed in alpha 1

- Fixed the 2.9.8 compiler-blocking string literals in `MainWindow.xaml.cs`.
- Added the first `Core/Eventing` abstraction and thread-safe event bus.
- Added a neutral diagnostics result model for the upcoming startup checks.
- Updated the project version to `3.0.0-alpha.1`.

## Completed since architecture review

- P0 unit tests for `StreamWorkflowService`, `AlertEngine`, licensing/`FeatureGate`, table-driven `SettingsValidator`, `AutomationRuleEngine`, `SecretJsonStore`.
- `IAlertRenderer` for testable alert playback.
- Extracted `MultiPcAgentClient`, `StreamerBotClient`, `MusicPlayerUiPresenter` from MainWindow orchestration.
- MVVM foundation (`ViewModelBase`, `RelayCommand`, `NavigationService`) and first `DiagnosticsPageViewModel`.
- Module self-registration via `IModuleRegistration` + `AddStreamingModules()`.
- Dedup: `SecretJsonStore<T>`, shared `AutomationRuleEngine`, `NowPlayingWidget`, Agent `WithObsControl` helper.
- `IEventBus` registered; `AppEventBridge` publishes workflow/music events (replaces dead stub).

## Next migration steps

1. Continue extracting MainWindow domains into services (overlay import, timed automation → `IAutomationRuleEngine`).
2. Move remaining navigation pages into dedicated Views/ViewModels; MainWindow becomes shell-only.
3. Replace remaining `DispatcherTimer` polling with EventBus/service events.
4. Widen Agent OBS routes and Multi-PC calls onto the shared client helpers.
5. Add P1 tests (OBS client protocol, API clients, OverlayDataService, IPC router).

The existing UI remains intact during migration so each alpha can be tested independently.
