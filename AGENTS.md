## Learned User Preferences

- Antworten auf Deutsch, kurz und direkt.
- Agent-Skills so ablegen, dass Cursor und Codex denselben Skill nutzen (canonical unter `.agents/skills/`, Kompatibilität über Symlinks).
- Overlay-Widget-Skill (`.agents/skills/overlay-widget/`) stets mitpflegen, wenn sich der Ablauf oder die Dateien zum Einführen von Overlay-Widgets/Shapes ändern. Effect-Modifier: `.agents/skills/overlay-effect/`; ZIP-Packs: `.agents/skills/overlay-extension-pack/`.
- Overlay-Widgets/Shapes mit maximaler Flexibilität: viele Design-Variants, Size-Presets und Config-Props (`colorProp`/`fontProp`/`featureSection`); Layout-Padding konfigurierbar; App generell erweiterbar halten.
- Overlay-Effects: Content- und Box/Container-Targets; nur Effekte anbieten, die im aktuellen Target greifen; Animationen dürfen den Content-Modus nicht überschreiben.
- Overlay-Editor: Raster immer im Vordergrund, Default-Zellen am Canvas-Seitenverhältnis (z. B. 32×18 bei 16:9); Settings-Controls einheitlich stylen (Zahlenfelder/Checkboxen analog Inner-Glow); Color-Input mit Expand-Handle und rechtbündiger Historien-Palette.
- Die App wird testgetrieben (TDD) entwickelt: zuerst Tests schreiben, dann Implementation; Änderungen ohne passende Tests vermeiden.
- Projektdokumentation unter `docs/` halten und thematisch in Unterordner sortieren; Changelogs unter `docs/changelogs/`.
- Root soll schlank bleiben (`README.md`); offene Docs und Notizen nicht im Repo-Root lassen.
- Auf macOS restore/format lokal möglich (`EnableWindowsTargeting`); Vollbuild, Test und Release über CI/Windows.



## Learned Workspace Facts

- CastingCouch ist eine WPF-Desktop-App auf .NET 10 (`net10.0-windows`) mit parallelem Tauri-2-Stack unter `tauri-app/` (React/Tailwind/TanStack, Rust-Crates `ccs-core`/`ccs-overlay-server`/`ccs-modules`); `Directory.Build.props` setzt `EnableWindowsTargeting` außerhalb von Windows (restore/format auf macOS); WPF-Vollbuild/Test braucht Windows, CI auf `windows-latest` plus Tauri-Job Win/macOS.
- GitHub-Remote: `CastingCouchApp/CastingCouch`.
- Versionsquelle ist `Directory.Build.props` (`<Version>`) und `version.json` für Tauri; Changelogs liegen als `docs/changelogs/CHANGELOG-<version>.md`.
- Tauri-Doku: `docs/architecture/TAURI-MIGRATION.md`; Overlay-HTTP bleibt `127.0.0.1:8765`.
- Build-Ausgabe unter `artifacts/`; lokale Kurzbefehle über `Makefile` (`restore`/`build`/`test`/`publish`/`ci`/`release`/`format`).
- CI: `.github/workflows/build.yml` (Push/PR), Release: `.github/workflows/release.yml` (Tags `v*` / manuell) über `build/Build-Release.ps1`.
- Release-Skill canonical: `.agents/skills/release/`; Symlinks unter `.cursor/skills/release` und `.codex/skills/release`.
- Overlay-Widget-Skill canonical: `.agents/skills/overlay-widget/`; Effect-Skill: `.agents/skills/overlay-effect/`; Extension-Pack-Skill: `.agents/skills/overlay-extension-pack/` (Symlinks unter `.cursor/skills/` und `.codex/skills/`).
- Canvas Overlay ist TypeScript unter `src/CreatorControlSuite.Modules.Overlay/CanvasOverlay/`; Bundle via esbuild (`npm run build` / MSBuild-Target `BuildCanvasOverlay`); generierte Bundles (`runtime.js`/`editor.js`/…) sind gitignored; Node 18+ nötig für Overlay-Modul-Build; Browser-Dev mit Hot-Reload und Overlay-Server-Simulation via `make canvas-dev` bzw. `npm run dev` (Default-Port 8765).
- Release-Artefakte: ZIP, MSI und signiertes `update-manifest.json` via `build/New-UpdateArtifacts.ps1`; In-App-Updates über GitHub Releases (`LocalUpdateService`), Public Key unter `src/CreatorControlSuite.App/Keys/`, CI-Secret `UPDATE_SIGNING_KEY_PEM`.
- Docs-Struktur: `docs/architecture/`, `modules/`, `build/`, `releases/`, `changelogs/`, `licensing/`, `operations/`, `guides/` (Index: `docs/README.md`).
- `.gitignore` deckt `artifacts/`, Secrets (`.env`, `*.pfx`/`*.pem`, `appsettings.*.Local.json`) und `.cursor/hooks/state/` ab; `Keys/README.txt` und `*-public.pem` bleiben versionierbar, Codex-Skills unter `.codex/skills/` ebenfalls.
