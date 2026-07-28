# CastingCouch

Windows-Desktop-Suite für Creator-Workflows: Live-Dashboard, OBS, Twitch, Spotify, Alerts, Overlays, Stream Deck und Automatisierung — gebündelt in einer WPF-App.

Aktuelle Version: **8.0.0-alpha102** (siehe `Directory.Build.props`).

## Features

- Live-Dashboard mit Status für Stream, OBS, Twitch und Spotify
- Twitch-Chat, Events und Rollenkennzeichnung
- Spotify-Steuerung inkl. Cover/Titel
- OBS-Szenen, Audio und Stream-Steuerung
- Alerts, Overlays, Workflows und Stream-Deck-Anbindung
- Multi-PC-Agent (Alpha), Updater und WiX-Installer

## Voraussetzungen

- Windows (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (x64) — Runtime allein reicht nicht
- Optional: PowerShell 7 (`pwsh`) für die Skripte unter `build/`
- Optional: Make für die Kurzbefehle im `Makefile`

SDK prüfen:

```bash
dotnet --list-sdks
```

## Schnellstart (Build)

```bash
make restore
make build
make test
make publish
```

| Target | Bedeutung |
|--------|-----------|
| `make ci` | Restore + Build + Test |
| `make format` | C# Autoformat (Whitespace + Style via `.editorconfig`) |
| `make format-check` | Format prüfen ohne Dateien zu ändern |
| `make format-analyzers` | Optional Analyzer-Fixes (nicht Teil von `format`) |
| `make publish` | Self-contained Publish (`win-x64`) inkl. CommandClient + Updater |
| `make release` | Voller Release-Pfad über `build/Build-Release.ps1` (App + MSI) |
| `make clean` | Artefakte entfernen |

Ausgabe liegt unter `artifacts/` (u. a. `artifacts/publish/win-x64`, `artifacts/installer`).

Klassischer Windows-Pfad ohne Make:

```bat
build\Run-CleanRelease.cmd
```

## Projektstruktur

```
src/                 App, Core, Module, Agent, Updater, …
tests/               xUnit-Tests
build/               PowerShell-Build-/Release-Skripte
installer/           WiX-Installer
docs/                Dokumentation (siehe `docs/README.md`)
artifacts/           Build-Ausgabe (gitignored)
.agents/skills/      Agent-Skills (Cursor + Codex)
.github/workflows/   CI (Build) und Release
```

Module u. a.: OBS, Twitch, Spotify, Alerts, Overlay, Workflow, StreamDeck.

## CI & Release

- **Build:** `.github/workflows/build.yml` — bei Push/PR auf `main`/`master` (Windows): restore, build, test, publish
- **Release:** `.github/workflows/release.yml` — bei Tag `v*` oder manuell: `Build-Release.ps1`, Zip/MSI, GitHub Release

Release-Tag Beispiel:

```bash
git tag v8.0.0-alpha101
git push origin v8.0.0-alpha101
```

Agent-gestützter Release-Flow: Skill `release` unter `.agents/skills/release/` (Symlinks für Cursor/Codex).

## Dokumentation

Übersicht: [`docs/README.md`](docs/README.md)

| Bereich | Pfad |
|---------|------|
| Architektur | [`docs/architecture/`](docs/architecture/) |
| Module | [`docs/modules/`](docs/modules/) |
| Build / Installer | [`docs/build/`](docs/build/) |
| Releases | [`docs/releases/`](docs/releases/) |
| Changelogs | [`docs/changelogs/`](docs/changelogs/) |
| Legal / Open Source | [`docs/licensing/`](docs/licensing/) |
| Betrieb | [`docs/operations/`](docs/operations/) |
| Guides | [`docs/guides/`](docs/guides/) |

## Hinweise

- Ziel-Framework: `net10.0-windows` (WPF) — lokaler Vollbuild nur unter Windows
- Lokale Secrets (`appsettings.Local.json`, `.env`, Zertifikate) gehören nicht ins Repo
- Profil-/Runtime-Daten werden getrennt vom Installer verwaltet
