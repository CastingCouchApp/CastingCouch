# Tauri-Migration — Status und Verträge

Stand: 30. August 2026

Agent-Prompts für die nächsten Slices: [`TAURI-PHASE-PROMPTS.md`](TAURI-PHASE-PROMPTS.md).

CastingCouch wird strangler-artig von WPF/.NET 10 nach **Tauri 2** (Windows + macOS) portiert.
Die WPF-App bleibt bis zur Feature-Parität das produktive Release; `tauri-app/` ist der neue Stack.

## Layout

```
tauri-app/
  src/                 React + Tailwind + TanStack (Router, Query, Table, Form)
  src-tauri/           Tauri-Host + Workspace-Crates
    crates/ccs-core
    crates/ccs-secrets
    crates/ccs-overlay-server
    crates/ccs-modules
src/                   WPF/.NET (legacy, weiter CI-gebaut)
src/.../CanvasOverlay  Overlay-Frontend (vanilla TS, OBS-kompatibel)
version.json           Versionsquelle für Tauri (`8.0.0-beta1` ↔ `8.0.0-beta.1`)
```

## Kommunikationsverträge

| Kanal | Zweck |
|-------|--------|
| Tauri `invoke` | Settings, Canvas-CRUD, Service-Status, Alerts |
| Tauri Events | `service-status`, `twitch-event`, `obs-scene` → React Query Cache |
| HTTP/WS `127.0.0.1:8765` | Overlay Editor/View/Solo, identische Routen wie Kestrel |

### Overlay-Routen (kompatibel)

`/health` `/ws` `/layout/{id}` `/data/overlay-data.json` `/canvas/*` `/editor` `/view` `/w/{type}` `/extensions` `/assets` `/obs/video-settings` `/obs/preview` `/chat` `/chat/config` `/chat/history`

## Datenpfade

Gleicher Ordner wie WPF: `%LocalAppData%/CreatorControlSuite` bzw. `~/Library/Application Support/CreatorControlSuite`.
Datei: `settings.json` (SchemaVersion 2, PascalCase). Secrets: OS-Keyring statt DPAPI.

## Modul-Status

| Modul | Status |
|-------|--------|
| Overlay-Server (HTTP/WS) | Rust, Route-Contract-Tests |
| Settings/Paths/Lock/Logging | `ccs-core` |
| Secrets | `ccs-secrets` (keyring) |
| OBS WebSocket 5 Live-Connect | `ccs-modules` (Auth, GetSceneList/SetScene, Reconnect, `CurrentProgramSceneChanged` → Overlay-Hub) |
| Twitch / Spotify | Twitch Device-Code + Helix-Status + EventSub-WS (follow/sub/cheer/…); Spotify PKCE-OAuth + currently-playing |
| Alerts | Persistenz in `settings.json` (WPF-PascalCase `Alerts.Definitions`); Runtime-Queue → Overlay-Hub `app.alert` |
| Overlay Event Bridge | Hub-Publish als C#-`OverlayRealtimeEvent` (camelCase `source`/`type`/`at`/`summary`/`data`) |
| YouTube Music / Workflow / Agent | Sidecar-Fallback (siehe unten) |
| Updates | SHA-256-Manifest-Verifier (`ccs-core::updates`) |
| Haupt-UI | Dashboard, Dienste, Overlay-Tabelle, Alerts-Library (TanStack Table), Settings, Updates, About |

## Sidecar (Übergang)

Komplexe Rest-Module (YouTube Music, Workflow-Designer, Multi-PC-Agent) bleiben in .NET, bis Rust-Parität steht.
Der Tauri-Host kann später einen lokalen HTTP-Sidecar starten (`CreatorControlSuite.exe` / Agent). Schnittstelle: JSON über Loopback, nicht Named Pipes.

## Cutover (Phase 6, vorbereitet)

Solange Overlay/OBS/Twitch in Tauri nicht feature-paritätisch sind:

- WPF bleibt in `.github/workflows/build.yml` Job `dotnet` + `package`.
- Tauri läuft zusätzlich (`tauri` Job, Windows + macOS).
- Default-Makefile: `make ci` = .NET; `make tauri-ci` = Tauri+Overlay-Frontend.
- Nach Parität: WPF-Jobs auf `legacy` setzen, `src/CreatorControlSuite.App` nach `legacy/` verschieben.

## Versionen

- .NET: `Directory.Build.props` `<Version>8.0.0-beta1</Version>`
- Tauri: `version.json` + `tauri-app/src-tauri/tauri.conf.json` (`8.0.0-beta.1`, SemVer für Bundler)
- Sync: `./scripts/sync-tauri-version.ps1`
