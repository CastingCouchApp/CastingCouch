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
| Tauri `invoke` | Settings, Canvas-CRUD (Layout-Datei + Rollback), `open_overlay_editor`, Service-Status, Alerts |
| Tauri Events | `service-status`, `twitch-event`, `obs-scene`, `now-playing` → React Query Cache (15s-Poll-Fallback) |
| HTTP/WS `127.0.0.1:8765` | Overlay Editor/View/Solo, identische Routen wie Kestrel |

### Overlay-Routen (kompatibel)

`/health` `/ws` `/layout/{id}` `/data/overlay-data.json` `/canvas/*` `/editor` `/editor/{id}` `/view` `/view/{id}` `/w/{type}` `/extensions` `/assets` `/obs/video-settings` `/obs/preview` `/chat` `/chat/config` `/chat/history`

## Datenpfade

Gleicher Ordner wie WPF: `%LocalAppData%/CreatorControlSuite` bzw. `~/Library/Application Support/CreatorControlSuite`.
Datei: `settings.json` (SchemaVersion 2, PascalCase). Secrets: OS-Keyring statt DPAPI.

## Modul-Status

| Modul | Status |
|-------|--------|
| Overlay-Server (HTTP/WS) | Rust, Route-Contract-Tests, Layout-Store unter `Overlay/layouts` |
| Settings/Paths/Lock/Logging | `ccs-core` |
| Secrets | `ccs-secrets` (keyring) |
| OBS WebSocket 5 Live-Connect | `ccs-modules` (Auth, GetSceneList/SetScene, Reconnect, `CurrentProgramSceneChanged` → Overlay-Hub) |
| Twitch / Spotify | Twitch Device-Code + Helix-Status + EventSub-WS (follow/sub/cheer/…); Spotify PKCE-OAuth + currently-playing |
| Alerts | Persistenz in `settings.json` (WPF-PascalCase `Alerts.Definitions`); Runtime-Queue → Overlay-Hub `app.alert` |
| Overlay Event Bridge | Hub-Publish als C#-`OverlayRealtimeEvent` (camelCase `source`/`type`/`at`/`summary`/`data`) |
| YouTube Music / Workflow / Agent | Sidecar-Fallback + dünne UI `/music` (Spotify + YTM) und `/workflow` (Schritt ausführen) |
| Updates | SHA-256 + RSA-Manifest-Verifier (`ccs-core::updates`); GitHub-Check + Download; Apply startet NSIS/MSI/DMG nach Backup; Tag-Pipeline in Phase 5 |
| Haupt-UI | Dashboard Live (OBS-Szene, Twitch-Login, Spotify Now Playing; Events + 15s-Fallback), Dienste (Connect/Login/Logout + Fehlerdetail), Musik (`/music`: Spotify always, YTM-Karte nur bei Sidecar healthy), Workflow (`/workflow`: Status + Schritt `workflow.*`), Overlay-Canvas-Tabelle (TanStack Table, Duplicate kopiert Layout, Editor-WebView auf `/editor/{id}`), Alerts-Library (TanStack Table), Settings-Formulare (General/OBS/Twitch/Spotify/Overlay/Branding, `data-theme` CSS-Tokens), Updates (Prüfen/RSA+SHA-256/Installer), About (Version, Datenpfad, Overlay-Health) |

## Sidecar (Übergang)

Komplexe Rest-Module (YouTube Music, Workflow-Schritt, Multi-PC-Agent) bleiben in .NET, bis Rust-Parität steht.
Der Tauri-Host spawnt optional `CreatorControlSuite.CommandClient.exe --sidecar --port 18765` (Windows, Loopback-JSON), wenn `Sidecar.Enabled` oder `CCS_SIDECAR=1` gesetzt ist und die Binary existiert. macOS überspringt den Spawn. Vertrag: [`TAURI-SIDECAR.md`](TAURI-SIDECAR.md).

## Cutover (Phase 6, vorbereitet)

Solange Overlay/OBS/Twitch in Tauri nicht feature-paritätisch sind:

- WPF bleibt in `.github/workflows/build.yml` Job `dotnet` + `package`.
- Tauri läuft zusätzlich (`tauri` Job, Windows + macOS, `--bundles none`).
- Tag-Release (`.github/workflows/release.yml`): WPF-ZIP/MSI **und** Tauri-NSIS/MSI/DMG.
- Default-Makefile: `make ci` = .NET; `make tauri-ci` = Tauri+Overlay-Frontend; `make tauri-release` = Installer nach `artifacts/tauri`.
- Nach Parität: WPF-Jobs auf `legacy` setzen, `src/CreatorControlSuite.App` nach `legacy/` verschieben.

## Versionen

- .NET: `Directory.Build.props` `<Version>8.0.0-beta1</Version>`
- Tauri: `version.json` + `tauri-app/src-tauri/tauri.conf.json` (`8.0.0-beta.1`, SemVer für Bundler)
- Sync: `./scripts/sync-tauri-version.ps1`
