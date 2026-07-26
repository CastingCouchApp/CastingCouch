## Learned User Preferences

- Antworten auf Deutsch, kurz und direkt.
- Agent-Skills so ablegen, dass Cursor und Codex denselben Skill nutzen (canonical unter `.agents/skills/`, Kompatibilität über Symlinks).
- Projektdokumentation unter `docs/` halten und thematisch in Unterordner sortieren; Changelogs unter `docs/changelogs/`.
- Root soll schlank bleiben (`README.md`); offene Docs und Notizen nicht im Repo-Root lassen.

## Learned Workspace Facts

- Creator Control Suite ist eine WPF-Desktop-App auf .NET 10 (`net10.0-windows`); lokaler Vollbuild/Test braucht Windows, CI läuft auf `windows-latest`.
- Versionsquelle ist `Directory.Build.props` (`<Version>`); Changelogs liegen als `docs/changelogs/CHANGELOG-<version>.md`.
- Build-Ausgabe unter `artifacts/`; lokale Kurzbefehle über `Makefile` (`restore`/`build`/`test`/`publish`/`ci`/`release`).
- CI: `.github/workflows/build.yml` (Push/PR), Release: `.github/workflows/release.yml` (Tags `v*` / manuell) über `build/Build-Release.ps1`.
- Release-Skill canonical: `.agents/skills/release/`; Symlinks unter `.cursor/skills/release` und `.codex/skills/release`.
- Docs-Struktur: `docs/architecture/`, `modules/`, `build/`, `releases/`, `changelogs/`, `licensing/`, `operations/`, `guides/` (Index: `docs/README.md`).
- `.gitignore` deckt `artifacts/`, Secrets (`.env`, `*.pfx`/`*.pem`, `appsettings.*.Local.json`) und `.cursor/hooks/state/` ab; `Keys/README.txt` und `*-public.pem` bleiben versionierbar, Codex-Skills unter `.codex/skills/` ebenfalls.
