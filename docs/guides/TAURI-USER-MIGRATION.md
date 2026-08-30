# Migration WPF → Tauri (User)

CastingCouch 8.x kann parallel als **WPF** (Windows) und **Tauri** (Windows + macOS) laufen.

## Daten

Einstellungen, Overlay-Layouts und Chat-History bleiben unter:

- Windows: `%LocalAppData%\CreatorControlSuite\`
- macOS: `~/Library/Application Support/CreatorControlSuite/`

Die Tauri-App liest `settings.json` (Schema 2) ohne Konvertierung. OAuth-Tokens wandern vom Windows-DPAPI-Store in den System-Keyring beim ersten Speichern eines Secrets.

## Overlay / OBS

Browser-Sources ändern sich nicht:

- `http://127.0.0.1:8765/view/{canvasId}`
- `http://127.0.0.1:8765/w/{widget}`

Nur eine App-Instanz sollte den Overlay-Port 8765 binden.

## Updates

GitHub Releases tragen parallele Manifeste bei gleicher ProductId `CreatorControlSuite`:

- WPF: `update-manifest.json` → ZIP
- Tauri Windows: `update-manifest-tauri-win.json` → NSIS-Setup
- Tauri macOS: `update-manifest-tauri-macos.json` → DMG

Kanal in `settings.json` bleibt `Alpha`/`Beta`/`Stable` (version-abgeleitet) und ist geteilt. Die Tauri-App prüft RSA-Signatur und SHA-256, sichert den Installationsordner nach `Backups/{version}` und startet den Installer. macOS-DMGs in Phase 5 ohne Apple-Notarize. MSI/NSIS (Windows) und DMG (macOS) kommen aus `tauri build` bzw. der Tag-Pipeline.
