## Learned User Preferences

- Antworten auf Deutsch, kurz und direkt.
- Agent-Skills so ablegen, dass Cursor und Codex denselben Skill nutzen (canonical unter `.agents/skills/`, Kompatibilität über Symlinks).
- Overlay-Widget-Skill (`.agents/skills/overlay-widget/`) stets mitpflegen, wenn sich der Ablauf oder die Dateien zum Einführen von Overlay-Widgets/Shapes ändern. Effect-Modifier: `.agents/skills/overlay-effect/`; ZIP-Packs: `.agents/skills/overlay-extension-pack/`.
- Die App wird testgetrieben (TDD) entwickelt: zuerst Tests schreiben, dann Implementation; Änderungen ohne passende Tests vermeiden.
- Projektdokumentation unter `docs/` halten und thematisch in Unterordner sortieren; Changelogs unter `docs/changelogs/`.
- Root soll schlank bleiben (`README.md`); offene Docs und Notizen nicht im Repo-Root lassen.
- Auf macOS fehlende Windows-/dotnet-Tools im Release-/Test-Flow überspringen und CI/Windows nutzen.



## Learned Workspace Facts

- Creator Control Suite ist eine WPF-Desktop-App auf .NET 10 (`net10.0-windows`); lokaler Vollbuild/Test braucht Windows, CI läuft auf `windows-latest`.
- GitHub-Remote: `frankhildebrandt/CreatorControlSuite`.
- Versionsquelle ist `Directory.Build.props` (`<Version>`); Changelogs liegen als `docs/changelogs/CHANGELOG-<version>.md`.
- Build-Ausgabe unter `artifacts/`; lokale Kurzbefehle über `Makefile` (`restore`/`build`/`test`/`publish`/`ci`/`release`).
- CI: `.github/workflows/build.yml` (Push/PR), Release: `.github/workflows/release.yml` (Tags `v*` / manuell) über `build/Build-Release.ps1`.
- Release-Skill canonical: `.agents/skills/release/`; Symlinks unter `.cursor/skills/release` und `.codex/skills/release`.
- Overlay-Widget-Skill canonical: `.agents/skills/overlay-widget/`; Effect-Skill: `.agents/skills/overlay-effect/`; Extension-Pack-Skill: `.agents/skills/overlay-extension-pack/` (Symlinks unter `.cursor/skills/` und `.codex/skills/`).
- Canvas Overlay ist TypeScript unter `CanvasOverlay/src/`, Bundle via esbuild (`npm run build` / MSBuild-Target `BuildCanvasOverlay`); generierte Bundles (`runtime.js`/`editor.js`/…) sind gitignored; Node 18+ nötig für Overlay-Modul-Build.
- Release-Artefakte: ZIP, MSI und signiertes `update-manifest.json` via `build/New-UpdateArtifacts.ps1`; In-App-Updates über GitHub Releases (`LocalUpdateService`), Public Key unter `src/CreatorControlSuite.App/Keys/`, CI-Secret `UPDATE_SIGNING_KEY_PEM`.
- Docs-Struktur: `docs/architecture/`, `modules/`, `build/`, `releases/`, `changelogs/`, `licensing/`, `operations/`, `guides/` (Index: `docs/README.md`).
- `.gitignore` deckt `artifacts/`, Secrets (`.env`, `*.pfx`/`*.pem`, `appsettings.*.Local.json`) und `.cursor/hooks/state/` ab; `Keys/README.txt` und `*-public.pem` bleiben versionierbar, Codex-Skills unter `.codex/skills/` ebenfalls.

