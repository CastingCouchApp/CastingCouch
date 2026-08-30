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

Weiterhin GitHub Releases mit `update-manifest.json` (SHA-256). MSI/NSIS (Windows) und DMG (macOS) kommen aus `tauri build`.
