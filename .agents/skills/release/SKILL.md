---
name: release
description: >-
  Führt den Creator-Control-Suite-Release-Flow aus: Änderungen committen,
  lokalen Test und Build babysitten, Versionsbump, erneut committen und pushen,
  CI online babysitten, GitHub-Release anlegen. Nutzen bei Release, Version
  bump, Tag, Ship, alpha-Release oder „Release machen“.
---

# Release (CastingCouch)

Führe den Flow strikt der Reihe nach aus. Bei Rot: fixen, erneut prüfen, erst dann weiter. Nicht überspringen.

## Fortschritt

```
Release Progress:
- [ ] 1. Alle Changes committen
- [ ] 2. Lokalen Test babysitten
- [ ] 3. Lokalen Build babysitten
- [ ] 4. Alles grün?
- [ ] 5. Release-Bump erzeugen
- [ ] 6. Changes noch mal committen und pushen
- [ ] 7. Online babysitten
- [ ] 8. Release anlegen
```

## Voraussetzungen

- Windows-Host für lokale Gates (WPF / `net10.0-windows` **und** Tauri-NSIS/MSI). Ohne Windows: **stoppen und nachfragen**, ob CI-only erlaubt ist.
- `.NET 10` SDK, Node 18+, Rust stable, `git`, `gh` (auth).
- Secrets (`.env`, `*.pfx`, Keys) nie committen.
- Tauri-Gates sind **Pflicht** (Tag-Pipeline erzeugt immer Tauri-Installer). Escape nur mit explizitem User-Wunsch und `SKIP_TAURI_GATES=1` (dann WPF-only, DMG/NSIS fehlen im Release).

## 1. Alle Changes committen

1. `git status`, `git diff`, `git log -5 --oneline` parallel.
2. Sinnvolle Commits (Conventional Commits: `feat`/`fix`/`chore`/`docs`/`test`/`ci`).
3. Keine Secrets. Working tree danach clean (außer bewusst ausgelassene Dateien).
4. **Noch nicht pushen.**

## 2. Lokalen Test babysitten

```bash
make test
# oder:
dotnet test tests/CreatorControlSuite.Tests/CreatorControlSuite.Tests.csproj -c Release
```

Tauri (Pflicht auf Windows, außer `SKIP_TAURI_GATES=1`):

```bash
make tauri-test
```

Bei Fehler: Ursache fixen → erneut testen → wiederholen bis grün. Keine Test-Abschaltung nur zum Grünmachen.

## 3. Lokalen Build babysitten

```bash
make publish
# voller WPF-Release-Pfad (inkl. MSI, braucht WiX/pwsh):
make release
```

Tauri-Installer (Pflicht auf Windows, außer `SKIP_TAURI_GATES=1`):

```bash
make tauri-release
# gleichwertig:
pwsh ./build/Build-Tauri-Release.ps1
```

Erzeugt NSIS + MSI unter `artifacts/tauri/` (`CastingCouch-{version}-win-x64-setup.exe` / `…-win-x64.msi`).

**macOS-DMG:** lokal nur auf einem Mac (`make build-dmg` / `make tauri-release`). Auf Windows nur Hinweis: DMG kommt aus CI-Job `tauri` auf `macos-latest`. Nicht überspringen und so tun, als wäre das DMG lokal gebaut.

Bei Fehler: fixen → erneut bauen → wiederholen bis grün.

## 4. Wenn alles grün

Nur wenn Schritt 2 und 3 grün sind (inkl. Tauri-Gates, sofern nicht übersprungen): weiter. Sonst bei Schritt 2/3 bleiben.

## 5. Release-Bump erzeugen

Quelle der Wahrheit: `Directory.Build.props` → `<Version>…</Version>`.

Aktuelles Schema: `8.0.0-alphaN` (Beispiel: `8.0.0-alpha101`).

1. Nächste Version ermitteln:
   - Default: `alphaN` → `alpha(N+1)`
   - Explizite User-Version hat Vorrang (z. B. `8.0.0`, `8.0.0-beta1`)
2. `Directory.Build.props` aktualisieren und `version.json` (`version` + `tauriVersion`) anpassen; `pwsh ./scripts/sync-tauri-version.ps1` ausführen.
3. Changelog anlegen: `docs/changelogs/CHANGELOG-<version>.md` mit kurzer Zusammenfassung der Änderungen seit dem letzten Release-Commit.
4. Keine anderen Dateien „auf Verdacht“ bump’en.

## 6. Changes noch mal committen und pushen

1. Bump + Changelog committen, z. B.:
   ```
   chore(release): bump version to <version>
   ```
2. Branch pushen: `git push -u origin HEAD` (bei Bedarf).
3. Kein Force-Push auf `main`/`master`.

## 7. Online babysitten

1. CI beobachten:
   ```bash
   gh run list --branch "$(git branch --show-current)" --limit 5
   gh run watch
   ```
2. Workflows: `Build` (`.github/workflows/build.yml`) — Tauri-Job dort mit `--bundles none` (keine Installer).
3. Bei Rot: Logs holen → fixen → committen → pushen → erneut watchen, bis grün.
4. Keine Workflow-Änderungen nur zum Grünfärben; wenn nötig, User fragen.

## 8. Release anlegen

1. Tag setzen und pushen (löst `.github/workflows/release.yml` aus):
   ```bash
   git tag "v<version>"
   git push origin "v<version>"
   ```
2. Release-Workflow babysitten:
   ```bash
   gh run list --workflow release.yml --limit 3
   gh run watch
   ```
   Jobs: `wpf` (ZIP/MSI + `update-manifest.json`), `tauri` (Windows NSIS/MSI + macOS DMG), `github-release` (signiert Tauri-Manifeste, hängt alles an).
3. Prüfen, dass GitHub Release Zip/MSI **und** Tauri-Installer plus Manifeste enthält:
   ```bash
   gh release view "v<version>"
   ```
   Erwartet u. a.:
   - `CreatorControlSuite-{version}-win-x64.zip`
   - `CreatorControlSuite-{version}-x64.msi`
   - `update-manifest.json` (WPF)
   - `CastingCouch-{version}-win-x64-setup.exe` (+ Tauri-MSI)
   - `CastingCouch-{version}-macos.dmg`
   - `update-manifest-tauri-win.json`
   - `update-manifest-tauri-macos.json`
   Erforderliches Secret für signierte Updates: `UPDATE_SIGNING_KEY_PEM` (PEM-Inhalt des Private Keys).
4. Falls Tag-Workflow keinen Release erzeugt hat: mit `gh release create` nachziehen und Artifacts aus dem Run anhängen — nur wenn klar fehlt.

## Abbruchbedingungen

- Lokale Gates ohne Windows und User erlaubt CI-only nicht.
- Unklare Versionsentscheidung (Breaking vs. alpha) → nachfragen.
- Merge-Konflikte oder geheime Dateien im Diff → stoppen und melden.

## Kurz-Antwort am Ende

Melde knapp: Version, Commit-Hashes (Bump), CI-Status, Release-URL (`gh release view --json url -q .url`).
