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

Mehrere Pfade sind in Tauri nur gemountet, nicht verhaltensgleich (501/204/Hardcode) — siehe Cutover-Blocker.

## Datenpfade

Gleicher Ordner wie WPF: `%LocalAppData%/CreatorControlSuite` bzw. `~/Library/Application Support/CreatorControlSuite`.
Datei: `settings.json` (SchemaVersion 2, PascalCase). Secrets: OS-Keyring statt DPAPI.

## Modul-Status

| Modul | Status |
|-------|--------|
| Overlay-Server (HTTP/WS) | Rust, Route-Contract-Tests, Layout-Store unter `Overlay/layouts`. **Stubs:** Asset-Upload, Extension-Install, `/obs/preview`, `/obs/video-settings` (kein Live-OBS), `/chat`, `/health`-Widget-Liste unvollständig |
| Settings/Paths/Lock/Logging | `ccs-core` |
| Secrets | `ccs-secrets` (keyring) |
| OBS WebSocket 5 Live-Connect | `ccs-modules` (Auth, GetSceneList/SetScene, Reconnect, `CurrentProgramSceneChanged` → Overlay-Hub). **Kein** `GetVideoSettings` / Screenshot, keine Sources/Audio/Stream-Steuerung |
| Twitch / Spotify | Twitch Device-Code + Helix `GET /users` + EventSub-WS (follow/sub/cheer/raid incoming). **Kein** Chat (`channel.chat.message`), Helix jenseits User. Spotify PKCE-OAuth + currently-playing |
| Alerts | Persistenz in `settings.json` (WPF-PascalCase `Alerts.Definitions`); Runtime-Queue → Overlay-Hub `app.alert` |
| Overlay Event Bridge | Hub-Publish als C#-`OverlayRealtimeEvent` (camelCase `source`/`type`/`at`/`summary`/`data`) |
| YouTube Music / Workflow / Agent | Sidecar-Fallback + dünne UI `/music` (Spotify + YTM) und `/workflow` (Schritt ausführen) |
| Updates | SHA-256 + RSA-Manifest-Verifier (`ccs-core::updates`); GitHub-Check + Download; Apply startet NSIS/MSI/DMG nach Backup; Tag-Pipeline in Phase 5 |
| Haupt-UI | Dashboard Live (OBS-Szene, Twitch-Login, Spotify Now Playing; Events + 15s-Fallback), Dienste (Connect/Login/Logout + Fehlerdetail), Musik (`/music`: Spotify always, YTM-Karte nur bei Sidecar healthy), Workflow (`/workflow`: Status + Schritt `workflow.*`), Overlay-Canvas-Tabelle (TanStack Table, Duplicate kopiert Layout, Editor-WebView auf `/editor/{id}`), Alerts-Library (TanStack Table), Settings-Formulare (General/OBS/Twitch/Spotify/Overlay/Branding, `data-theme` CSS-Tokens), Updates (Prüfen/RSA+SHA-256/Installer), About (Version, Datenpfad, Overlay-Health) |

## Sidecar (Übergang)

Komplexe Rest-Module (YouTube Music, Workflow-Schritt, Multi-PC-Agent) bleiben in .NET, bis Rust-Parität steht.
Der Tauri-Host spawnt optional `CreatorControlSuite.CommandClient.exe --sidecar --port 18765` (Windows, Loopback-JSON), wenn `Sidecar.Enabled` oder `CCS_SIDECAR=1` gesetzt ist und die Binary existiert. macOS überspringt den Spawn. Vertrag: [`TAURI-SIDECAR.md`](TAURI-SIDECAR.md).

## Cutover (Phase 6, gestoppt 30. Aug 2026)

**Nicht ausgeführt.** Overlay/OBS/Twitch sind nicht feature-paritätisch. CI, Makefile, App-Pfad, Release-Skill, User-Guide und Sidecar unverändert.

Unverändert (WPF bleibt Default):

- WPF in `.github/workflows/build.yml` Job `dotnet` + `package`.
- Tauri zusätzlich (`tauri` Job, Windows + macOS, `--bundles none`).
- Tag-Release (`.github/workflows/release.yml`): WPF-ZIP/MSI **und** Tauri-NSIS/MSI/DMG.
- Default-Makefile: `make ci` = .NET; `make tauri-ci` = Tauri+Overlay-Frontend; `make tauri-release` = Installer nach `artifacts/tauri`.
- Nach Parität: WPF-Jobs auf `legacy` setzen, `src/CreatorControlSuite.App` nach `legacy/` verschieben. Prompt 6 dann erneut.

### Blocker Overlay

| Route | Tauri | WPF |
|-------|-------|-----|
| `POST /extensions/install` | 501 | ZIP-Install |
| `DELETE /extensions/{id}` | 204, no-op | Uninstall |
| `POST /assets` | 501 | Bild-Upload |
| `DELETE /assets/{id}` | 204, no-op | Delete |
| `GET /obs/video-settings` | fest 1920×1080@60 | Live `GetVideoSettings` |
| `GET /obs/preview` | 204 | PNG Programmszene |
| `GET /chat` | HTML nur Titel | Standalone-Chat |
| `GET /chat/background` | 204 | Hintergrundbild |
| `GET /chat/history` | Datei oder `{messages:[]}`, kein Writer | `{events}` + Persistenz |
| `GET /data/overlay-config.json` | `{ok:true}` | echte Config oder `{}` |
| `GET /health` | Port immer 8765; Widget-Liste Teilmenge | Port aus Settings; volle Liste |

Weitere: kein EventSub-Chat ins Overlay; WS-Hello/Layout-PUT-Envelope weicht ab; Port-Änderung startet den Server nicht neu; `overlay-data.json` ohne Live-Writer.

### Blocker OBS

Nur Auth, `GetSceneList`, `SetCurrentProgramScene`, Reconnect, Program-Scene → Hub. Fehlend für Overlay-Kern: `GetVideoSettings`, `GetSourceScreenshot` (Editor-Preview). Professional (Sources, Audio, Stream/Record, Scene Items, Automation) ist kein Cutover-Gate, aber unportiert.

### Blocker Twitch

Kein `channel.chat.message` / delete / clear → Overlay-Chat tot. Helix nur `GET /users`. EventSub ohne Reconnect nach hartem Drop. React hört `twitch-event` nicht. Professional (Polls/Predictions/Rewards/Outgoing-Raid) ist kein Cutover-Gate.

### Vor erneutem Cutover (Kern, nicht Professional-Dashboard)

1. Overlay: Asset-Upload/Delete, Extension-Install/Uninstall, Live `/obs/video-settings` + `/obs/preview`, `/health`-Widgets = CanvasOverlay, Port nicht hardcoden.
2. Twitch: EventSub Chat + Fragments → Overlay-Hub; History-Writer im WPF-Format.
3. OBS: `GetVideoSettings` + `GetSourceScreenshot` live.

## Versionen

- .NET: `Directory.Build.props` `<Version>8.0.0-beta1</Version>`
- Tauri: `version.json` + `tauri-app/src-tauri/tauri.conf.json` (`8.0.0-beta.1`, SemVer für Bundler)
- Sync: `./scripts/sync-tauri-version.ps1`
