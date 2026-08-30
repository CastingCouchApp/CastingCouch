# Tauri-Migration — Agent-Prompts (nächste Phasen)

Stand: 30. August 2026

Copy-Paste-Prompts für neue Agent-Chats. **Ein Prompt pro Chat.** Phasen nicht überspringen, wenn sie aufeinander aufbauen.

Verträge: [`TAURI-MIGRATION.md`](TAURI-MIGRATION.md), Sidecar: [`TAURI-SIDECAR.md`](TAURI-SIDECAR.md), User: [`../guides/TAURI-USER-MIGRATION.md`](../guides/TAURI-USER-MIGRATION.md). Ursprungsplan: Phasen 0–6.

## Nutzung

- Antworten auf Deutsch, kurz, TDD (erst Tests, dann Code).
- WPF bleibt produktiv, bis Phase 6. Keine `MainWindow`-Orchestrierung portieren.
- Canvas Overlay bleibt vanilla TS + esbuild; React nur App-UI.
- Overlay-HTTP bleibt `127.0.0.1:8765`, gleiche Routen.
- Daten: `%LocalAppData%/CreatorControlSuite` bzw. `~/Library/Application Support/CreatorControlSuite`, `settings.json` Schema 2 PascalCase.
- Nach der Phase: Status-Matrix in `TAURI-MIGRATION.md` aktualisieren.

## Ist-Stand (Gerüst, keine Feature-Parität)

| Phase | Skeleton | Offen |
|-------|----------|--------|
| 0 Fundament | `tauri-app/`, CI-Job Win/macOS, Makefile | — |
| 1 Overlay-Server | Axum + Route-Contract-Tests, Hub, Layout-Store | Assets, Extension-Install, `/obs/preview`, Chat-HTML |
| 2 Core | Settings/Paths/Lock/Logging, Keyring, SHA-256-Verifier | Schema-Migrationen 1:1, Secret-Migration DPAPI→Keyring |
| 3 Module | OBS Live-Connect; Twitch Device-Code + Helix + EventSub; Spotify PKCE + currently-playing; Overlay-Bridge; Alerts Persistenz + Overlay-Runtime; Sidecar-Spawn | OBS Preview/VideoSettings; Twitch Chat-EventSub |
| 4 UI | Shell; Alerts-Library-Table; Settings/Services; Overlay-Tabelle + Editor-WebView; Dashboard Live (Szene/Twitch/Now Playing, Events + 15s-Fallback); Updates/About; Theme-Tokens (`data-theme`); Sidecar-UI (`/music`, `/workflow`) | pixel-perfekte Themes; `twitch-event`-Listener |
| 5 Release | `release.yml` (WPF + Tauri NSIS/MSI/DMG), signierte `update-manifest-tauri-*.json`, RSA-Verifier, Apply/Backup | Apple-Notarize |
| 6 Cutover | **gestoppt** (Parität fehlt, 30. Aug 2026) | Overlay-Stubs, Chat, OBS-Preview — siehe `TAURI-MIGRATION.md` |

---

## Prompt 3.1 — OBS Live-Connect

```
Implementiere Phase 3.1 der Tauri-Migration: OBS WebSocket 5 Live-Connect in Rust.

Kontext:
- Vertrag: docs/architecture/TAURI-MIGRATION.md
- Ist: tauri-app/src-tauri/crates/ccs-modules/src/obs.rs hat Identify-Handshake, connect() ohne Auth, kein Event-Loop.
- C#-Spezifikation: src/CreatorControlSuite.Modules.OBS/ (ObsWebSocketClient, Auth, Requests, Events). Fixtures/Tests unter tests/CreatorControlSuite.Tests als Vertrag.
- UI: tauri-app/src/routes/services.tsx ruft connect_obs auf; Dashboard pollt service_statuses.

Ziel:
1. OBS-Connect mit optionalem Passwort aus ccs-secrets (nicht in settings.json).
2. Hello → Identify inkl. Auth (WebSocket 5).
3. Request-Roundtrip: GetSceneList, SetCurrentProgramScene (bestehendes set_scene-Payload nutzen).
4. Persistente Verbindung + Reconnect; Status connected/connecting/disconnected/error über service_statuses.
5. Tauri-Commands: connect_obs, disconnect_obs, obs_scenes, obs_set_scene. Events später (3.4).
6. Passwort in Settings-UI noch nicht nötig — Command darf Secret-Key setzen/lesen.

TDD: Rust-Tests mit JSON-Frame-Fixtures (Hello/Identify/RequestResponse) analog C#. Keine echten OBS-Calls in Unit-Tests.
Nicht: Alerts, Overlay-Bridge, volle Professional-Dashboard-Parität.
Exit: cargo test --workspace grün; manuell connect_obs gegen lokales OBS; TAURI-MIGRATION.md OBS-Zeile aktualisieren.
```

---

## Prompt 3.2 — Twitch OAuth + Helix-Status

```
Implementiere Phase 3.2: Twitch-Login in Tauri (OAuth-WebView) und Helix-User-Status.

Kontext:
- UI sagt explizit „OAuth folgt in Phase 3.“ (tauri-app/src/routes/services.tsx).
- Ist: ccs-modules/src/twitch.rs hat Helix-URL und EventSub-URL, kein Token, kein HTTP.
- C#: TwitchModule, TwitchWebLoginWindow, ISecretStore/DPAPI. Tokens dürfen nicht in settings.json.
- Secrets: ccs-secrets Keyring. Settings: ChannelName, ClientId, AutoConnect.

Ziel:
1. OAuth-Authorization-Code (oder Device-Code, falls WebView-Redirect hakelig) in Tauri-WebView; Redirect lokal abfangen.
2. Access/Refresh-Token im Keyring; Client-Secret ebenfalls Keyring, nie loggen.
3. Helix GET /users → Channel-Id/Display-Name; Status connected wenn Token gültig.
4. Commands: twitch_login, twitch_logout, twitch_status (oder service_statuses erweitern).
5. Services-Page: Button Anmelden/Abmelden statt Placeholder-Text.
6. EventSub-WebSocket noch nicht (Phase 3.4).

TDD: Token-Store-Mocks, Helix-Fixture-Tests (Pflicht-Header Client-Id + Bearer). Vitest für Login-Button-Zustände mit mock invoke.
Nicht: Chat, Raids, Professional-Historie, EventSub.
Exit: Login speichert Token im Keyring; Logout löscht; bestehende WPF-settings.json bleibt lesbar.
```

---

## Prompt 3.3 — Spotify OAuth + Now Playing

```
Implementiere Phase 3.3: Spotify OAuth + currently-playing in Tauri.

Kontext:
- Ist: ccs-modules/src/spotify.rs hat token_url/currently_playing_url und set_now_playing (in-memory).
- Command now_playing existiert. Dashboard/Services zeigen nur Status.
- C#: SpotifyModule, Playback-Nullability, Bearer, Paging — Fixtures in tests/CreatorControlSuite.Tests.

Ziel:
1. OAuth analog Twitch (WebView + Keyring). PKCE falls möglich.
2. Poll oder einmaliger Fetch von /me/player/currently-playing → NowPlaying {title, artist, album, is_playing}.
3. Status connected bei gültigem Token; Fehler sichtbar in service_statuses.detail.
4. Commands: spotify_login, spotify_logout, now_playing (bestehend füllen).
5. Services-Page analog Twitch.

TDD: JSON-Fixtures für currently-playing (inkl. item=null / 204). Keine echten Spotify-Calls in CI.
Nicht: Geräteautomatik, Alert-Ducking, Playlists, YouTube Music.
Exit: Now Playing kommt nach Login; Token nicht in Logs/Settings.
```

---

## Prompt 3.4 — Overlay Event Bridge + Twitch EventSub

```
Implementiere Phase 3.4: Live-Events von Twitch (und optional OBS) in den Overlay-Hub.

Kontext:
- Hub: ccs-overlay-server RealtimeHub; ccs-modules overlay_bridge.
- Overlay-WS: /ws, gleiche Payloads wie WPF OverlayEventBridge / OverlayRealtimeHub.
- Twitch EventSub URL existiert als Konstante. OBS-Events fehlen nach 3.1.

Ziel:
1. EventSub-WebSocket nach erfolgreichem Twitch-Login; Session-Keepalive, Reconnect-URL.
2. Mindestens: channel.follow, channel.subscribe, channel.cheer (weitere Typen analog C# wenn Fixtures da).
3. Bridge published JSON auf den Overlay-Hub, damit /view und Chat-Widgets Updates sehen.
4. Tauri Events (app.emit) damit React Query per listen Cache aktualisiert — nicht nur Polling.
5. Alerts-Engine enqueue bei passendem event_type (in-memory ok, Persistenz in 3.5).

TDD: EventSub-Message-Fixtures; Hub-Publish-Test; Bridge mappt Twitch-Typ → Overlay-Payload wie C#.
Nicht: Alert-Renderer in OBS, volle Chat-Historie, Workflow.
Exit: Follow-Event erscheint im Overlay-WS; Dashboard kann Status ohne 4s-Polling aktualisieren.
```

---

## Prompt 3.5 — Alerts Persistenz + Runtime

```
Implementiere Phase 3.5: Alert-Definitionen persistieren und Runtime-Queue an Overlay/OBS anbinden.

Kontext:
- Ist: AlertEngine in-memory (list/upsert/enqueue). UI: tauri-app/src/routes/alerts.tsx dünn.
- C#: AlertDefinitionApplicationService, AlertEngine, OBS-Quellnamen, Streamer.bot-Unterdrückung.
- Settings.json enthält Alert-Defs im WPF-Schema — PascalCase beibehalten.

Ziel:
1. load/save Alert-Definitionen über JsonSettingsStore (kein eigenes File-Format).
2. Commands: list_alerts, upsert_alert, delete_alert, alert_runtime (pending_count, enabled).
3. Event aus 3.4 → passende enabled Definition → enqueue → Overlay-Hub Alert-Payload.
4. Optional: OBS-Browser-Source-Name aus Settings setzen (kein voller Professional-Slice).
5. Alerts-Page: TanStack Table für Library; Enable/Disable; keine MediaElement-Vorschau in v1.

TDD: Persistenz-Roundtrip, Rollback bei save-Fehler analog C# Create/Duplicate.
Nicht: Designer mit Audio/Video-Preview, Streamer.bot-Vollintegration.
Exit: Neustart der Tauri-App behält Alerts; Test-Event feuert Overlay-Payload.
```

---

## Prompt 3.6 — Sidecar-Spawn (YTM / Workflow / Agent)

```
Implementiere Phase 3.6: optionaler .NET-Sidecar laut docs/architecture/TAURI-SIDECAR.md.

Kontext:
- ccs-modules/src/sidecar.rs hat nur health_url. Host startet Sidecar nicht.
- Vertrag: GET /sidecar/health, POST /sidecar/workflow/run, GET /sidecar/ytm/now-playing.
- Loopback JSON, Port 18765. Kein Named Pipe auf macOS.
- Sidecar-Binary: CreatorControlSuite.CommandClient --sidecar --port 18765 (Windows). macOS: Sidecar überspringen oder dokumentieren.

Ziel:
1. Tauri-Host spawnt Sidecar nur wenn Settings/Flag es erlaubt und Binary existiert.
2. Health-Check; Commands sidecar_status, sidecar_ytm_now_playing (passt durch).
3. Kein Start, wenn Overlay-Port oder Sidecar-Port belegt — klare Fehler.
4. Tests: URL-Vertrag + Mock-HTTP; kein echtes .NET in CI nötig.
5. Docs: wann Sidecar nötig ist, in TAURI-SIDECAR.md schärfen.

Nicht: Workflow-Designer-UI, Agent-TLS, volles Run-of-Show.
Exit: Auf Windows mit gebautem CommandClient: health 200; ohne Binary: disconnected ohne Crash.
```

---

## Prompt 4.1 — Services- und Settings-UI (Form-Parität)

```
Implementiere Phase 4.1: Services- und Settings-Pages auf echte Formulare, nachdem 3.1–3.3 Login/Connect können.

Kontext:
- Stack: React 19, Tailwind, TanStack Router/Query/Form. Shared UI: components/ui/{button,card,input}.
- settings.tsx: Teilfelder General/OBS/Twitch/Branding. api.ts AppSettings unvollständig vs. ccs-core AppSettings.
- services.tsx: nur OBS-Button.

Ziel:
1. AppSettings-Typ 1:1 zum Rust-serde (PascalCase). Keine stillen Defaults, die WPF-Felder löschen.
2. Settings: TanStack Form, Sektionen General, OBS, Twitch, Spotify, Overlay, Branding. Save invalidiert Query.
3. ThemeId setzt data-theme auf <html> (CSS-Variablen; nicht alle 15 Themes pixel-perfekt).
4. Services: Connect/Login/Logout pro Dienst, Fehlerdetail, AutoConnect-Hinweis.
5. Vitest + Testing Library analog overlay-page.test.tsx; mock invoke in api.ts erweitern.

Nicht: Workflow, Music-Player-Page, Statistics.
Exit: Speichern rundtrippt settings.json ohne Feldverlust; make -C tauri-app test grün.
```

---

## Prompt 4.2 — Overlay-Verwaltung + Editor-Fenster

```
Implementiere Phase 4.2: Overlay-Canvas-UI und Editor als eigenes Tauri-WebView.

Kontext:
- overlay.tsx: HTML-Table, Create/Duplicate/Delete, URLs. Kein TanStack Table, kein Editor-Window.
- C#: OverlayCanvasApplicationService (Create/Duplicate Rollback), OverlayEditorWindow → http://127.0.0.1:8765/editor/{id}.
- Overlay-Server muss laufen (Phase 1). Layout-Dateien unter Overlay-Root.

Ziel:
1. Canvas-Liste als TanStack Table (Name, View-URL, Aktionen).
2. Duplicate kopiert Layout-Datei, nicht nur Metadaten (wie WPF). Delete: erst Settings, dann Datei.
3. „Editor öffnen“: neues Tauri-WebView/label auf editor_url (plugin-opener oder WebviewWindow).
4. View-URL kopieren; Health-Check overlay_health_url in der Page.
5. Tests: Duplicate-Rollback, URL-Format /editor/{id} /view/{id}.

Nicht: Widget-Palette in React, Extension-Pack-UI (eigener Slice).
Exit: Editor lädt CanvasOverlay; OBS-Browser-Source-URLs unverändert.
```

---

## Prompt 4.3 — Dashboard Live + Music-Karte

```
Implementiere Phase 4.3: Dashboard zeigt Live-Status ohne nur 4s-Polling.

Kontext:
- index.tsx pollt service_statuses. now_playing Command existiert.
- Nach 3.4: Tauri Events. Query-Konvention: listen → queryClient.setQueryData.

Ziel:
1. Dashboard: OBS-Szene (wenn 3.1), Twitch-Login-Name, Spotify Now Playing.
2. Subscribe auf Tauri-Events; Polling nur Fallback (länger, z. B. 15s).
3. Fehlerzustände und „disconnected“ klar.
4. Vitest für Karten bei leeren/fehlerhaften Services.

Nicht: Statistics-Historie, Workflow-Countdown, Professional-Graphs.
Exit: Track-Wechsel oder OBS-Connect aktualisiert UI ohne Reload.
```

---

## Prompt 4.4 — Restliche App-Routes (Updates, About, Themes)

```
Implementiere Phase 4.4: Updates- und About-Pages anbinden; Theme-Tokens.

Kontext:
- updates.rs: SHA-256-Manifest-Verifier, noch kein Download/Apply.
- C#: UpdateWorkflowService, UpdateSettingsView, GitHub Releases, Public Key unter src/CreatorControlSuite.App/Keys/.
- Themes: src/CreatorControlSuite.App/Themes/ + ThemeCatalog → CSS Variables. docs/architecture/UI-THEMES.md.

Ziel:
1. Updates-Page: aktuelle Version (version.json/tauri), Prüfen-Button, Manifest-Anzeige, Checksum-Fehler.
2. Download/Apply nur wenn Verifier grün; Backup optional analog WPF oder klar als „folgt“.
3. About: Version, Datenpfad (app_paths), Overlay-Health-Link.
4. Mindestens 4 Themes als data-theme Tokens (classic + die in settings.tsx gelisteten).
5. Release-Skill nur erwähnen, nicht umbauen (Phase 5).

TDD: verify_package-Fehlerpfade in UI; Manifest-Fixture.
Exit: Falsche SHA-256 wird abgelehnt; Theme-Wechsel sofort sichtbar.
```

---

## Prompt 4.5 — Workflow / Music / Agent über Sidecar (UI)

```
Implementiere Phase 4.5: dünne React-Pages für Sidecar-Features. Nur wenn 3.6 Sidecar-Spawn existiert.

Kontext:
- Sidecar-API: health, workflow/run, ytm/now-playing.
- WPF-Pages nicht 1:1 nachbauen. Keine MainWindow-Timer.

Ziel:
1. Route /music: Spotify now_playing plus YTM-Karte wenn Sidecar healthy, sonst Hinweis.
2. Route /workflow: Status + ein „Schritt ausführen“ gegen POST /sidecar/workflow/run (Payload laut C#-Vertrag, klein halten).
3. Navigation in AppShell. Sidecar down → leere States, kein Crash.
4. Tests mit gemocktem invoke/HTTP.

Nicht: Designer-Canvas, Timed-Automation-Engine in Rust, Agent-TLS-UI.
Exit: Ohne Sidecar nutzbar (Spotify-only); mit Sidecar YTM-Karte sichtbar.
```

---

## Prompt 5 — Release-Pipeline Tauri

```
Implementiere Phase 5: Tauri-Release analog WPF (NSIS/MSI + DMG + signiertes update-manifest).

Kontext:
- Lokal: make build-nsis / build-dmg, build/Build-Tauri-Release.ps1 kopiert nach artifacts/tauri.
- CI build.yml tauri-Job: --bundles none (kein Installer).
- WPF: .github/workflows/release.yml + build/Build-Release.ps1 + New-UpdateArtifacts.ps1, Secret UPDATE_SIGNING_KEY_PEM.
- Skill: .agents/skills/release/SKILL.md — Tauri bisher optional.
- Updater: ccs-core::updates Verifier; In-App-Apply aus 4.4.

Ziel:
1. release.yml (oder Job darin): Windows NSIS/MSI + macOS DMG; Artefakte uploaden.
2. update-manifest.json für Tauri-Pakete (SHA-256, gleiche Produkt-Id-Strategie dokumentieren — WPF vs Tauri Channel).
3. Signing wie WPF, Keys nicht committen.
4. Release-Skill: nach Feature-Parität oder Flag Tauri-Gates (make tauri-test, Build-Tauri-Release.ps1) als Pflicht auf Windows+Hinweis macOS/CI.
5. Makefile help/README stimmen mit dem echten Flow überein.

Nicht: WPF-MSI entfernen (das ist Phase 6).
Exit: Tag-Pipeline erzeugt Tauri-Installer + Manifest; Verifier akzeptiert das Paket.
```

---

## Prompt 6 — Cutover (erst nach Feature-Parität)

Letzter Versuch 30. Aug 2026: **STOP**. Lücken in [`TAURI-MIGRATION.md`](TAURI-MIGRATION.md) (Cutover-Blocker). Nicht erneut ausführen, bis Overlay-Stubs, EventSub-Chat und OBS-Preview geschlossen sind.

```
Implementiere Phase 6 nur wenn Overlay/OBS/Twitch in Tauri feature-paritätisch sind. Sonst STOPPEN und Lücken listen.

Cutover laut docs/architecture/TAURI-MIGRATION.md:
1. WPF-Jobs in build.yml auf legacy umbenennen oder default aus; Tauri wird Default-CI.
2. src/CreatorControlSuite.App nach legacy/ (oder dokumentierter Archiv-Pfad); Solution/CI anpassen.
3. make ci zeigt auf Tauri+Overlay; make tauri-ci entfällt oder wird Alias.
4. Release-Skill: Windows-Host-Gate für WPF entfernen bzw. durch Tauri-Gates ersetzen.
5. docs/guides/TAURI-USER-MIGRATION.md: WPF→Tauri als Default-Pfad, Port 8765 eine Instanz.
6. Sidecar nur noch für explizit nicht portierte Module; Rest entfernen oder hinter Flag.

Nicht: Datenformat brechen; Overlay-URLs ändern; Secrets aus Keyring löschen.
Exit: Frischer Clone baut/testet ohne WPF-Vollbuild; User-Guide beschreibt einen Startweg.
```

---

## Reihenfolge

```
3.1 OBS → 3.2 Twitch OAuth → 3.3 Spotify OAuth
                ↓
         3.4 Event Bridge
                ↓
         3.5 Alerts Persistenz
                ↓
         3.6 Sidecar-Spawn
                ↓
    4.1 Settings/Services UI  →  4.2 Overlay-Editor-Window
                ↓
         4.3 Dashboard Live  →  4.4 Updates/Themes
                ↓
         4.5 Sidecar-UI (optional)
                ↓
              5 Release
                ↓
         6 Cutover (nur nach Parität)
```

3.2 und 3.3 können parallel in zwei Chats laufen, sobald 3.1 nicht dieselben Dateien blockiert.
