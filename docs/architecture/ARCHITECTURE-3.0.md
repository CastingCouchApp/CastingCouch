# CastingCouch 3.0 architecture migration

This alpha starts the migration without removing existing functionality.

## Guardrails

- Hard limit: **no new or extracted App file ≥ 1000 LOC**. Split further (Models / Analyzer / Parts) instead of growing.
- `MainWindow` may only shrink; never add new domain logic there.
- ViewModels are remote controls only: Commands → Module/Core/App services, binding state, no business rules.
- Placement: pure policy → `Core`; one integration partner → `Modules.<X>`; multi-module / WPF orchestration → `App/Services`; UI only → `Views` / `ViewModels` / `Controls`.

## Target App layout

```
CreatorControlSuite.App/
  Shell/                 # MainWindow host (navigation + DI wiring)
  Views/
    Pages/<Domain>/      # page UserControls (+ Parts/ when XAML would exceed 1k)
    Dialogs/             # modal windows
  ViewModels/Pages/      # thin page VMs (IPageViewModel)
  Services/              # App orchestration (incl. CreatorIntelligence/)
  Controls/ Mvvm/ Themes/
```

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
- CreatorIntelligence moved to `Services/CreatorIntelligence/` (service + models + analysis split, each &lt;1k LOC).
- Dialog windows moved to `Views/Dialogs/`.
- Pure timed-automation schedule evaluation in `Core.Automation.TimedAutomationSchedule` (+ unit tests).
- `SpotifySavedStateStore` extracted from MainWindow.
- Page Views/VMs: Diagnostics, Profiles, About; Music + CreatorIntelligence section VMs; `PageNavigationCoordinator`.
- MainWindow moved to `Shell/`; timed-automation timer replaced by `TimedAutomationTickPublisher` → `IEventBus`.

## Next migration steps

1. Continue extracting MainWindow domains into services (remaining Spotify/OBS/Twitch orchestration, Services-page Parts).
2. Move remaining navigation pages into dedicated Views/ViewModels; shrink Shell further.
3. Replace remaining `DispatcherTimer` polling with EventBus/service events.
4. Widen Agent OBS routes and Multi-PC calls onto the shared client helpers.
5. Add P1 tests (OBS client protocol, API clients, OverlayDataService, IPC router).

The existing UI remains intact during migration so each alpha can be tested independently.
