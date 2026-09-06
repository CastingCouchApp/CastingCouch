# Tauri-Basis: Implementierung und Abnahme

Stand: 6. September 2026. Umfang: B1–B7 aus dem ausgewählten Paritätsplan. Die Implementierung und die automatisierte Prüfung sind von der noch offenen Betriebsabnahme getrennt.

| Paket | Nachweis |
|---|---|
| B1 | `tauri-app/scripts/generate-command-contract.mjs` erzeugt Namen, Argumente und sechs native Befehls-/Abfrage-Enums. `contracts:check` läuft vor Test und Build. TypeScript-Negativprüfungen verwerfen falsche Namen, Pflichtfelder und Typen. `src-tauri/src/command_tests.rs` durchläuft tatsächliche Command-Makros und Persistenz. Tauri-Fenster werden dafür simuliert. |
| B2 | `ccs-overlay-server/build.rs` bindet den gemeinsamen Canvas-Build in das Binary ein. `tests/distribution.rs` und echte HTTP-Tests prüfen die Auslieferung ohne Asset-Dateien im App-Datenverzeichnis. Der Win-/macOS-CI-Job baut Installationspakete und lädt sie als Artefakte hoch. |
| B3 | Native IPC reserviert einen echten Port und prüft erfolgreichen Wechsel sowie belegte Ports ohne Settings-Commit. Server-Stop schließt bestehende WebSockets. Die UI invalidiert abhängige Abfragen; Neuverbindungsfehler werden von Speicherfehlern unterschieden. Datenpfade berücksichtigen C#-Importpfade und Umgebungsvariablen. |
| B4 | `build/ParityFixtures` verwendet die tatsächlichen C#-Modelle. Leere und befüllte Einstellungen werden feldweise mit dem nach Rust-Laden/Speichern entstandenen Dokument verglichen. Weitere Tests prüfen unbekannte Felder, Listenidentitäten, unabhängige Änderungen und Konflikte. |
| B5 | Ein Kindprozess prüft die exklusive Instanzsperre. Der Lock-Dateiknoten bleibt zwischen Starts erhalten. Lokale WebSocket-Gegenstellen prüfen Verbindungsabbruch und neue EventSub-Subscriptions. HTTP-Gegenstellen prüfen Tokenablauf, Fehler und konkurrierende Erneuerung. Fatale Startfehler und Serverprobleme haben eine sichtbare UI. |
| B6 | Alle in `OverlayWebServer.cs` registrierten Route-Gruppen wurden abgeglichen. HTTP und WebSocket prüfen Hello, Layout-Speicherung/Neuladen, Standardlayout, ausgewählten Canvas, tatsächlichen Port, Chat-Konfiguration/History sowie Asset-CRUD. ZIP-Persistenz und Validierung besitzen zusätzliche Dateisystemtests; komplette Pack-Varianten bleiben O4. |
| B7 | Snapshot-Vertrag gegen das C#-Overlay-Modell; Ereignisse und getrennte Dienstabfragen aktualisieren den gemeinsamen Datenstand. Tests prüfen Ziele/Kanalzahlen, Sitzungszähler, Teilupdates und Zusatzfelder. Ein echter Hardlink weist nach, dass importierte Overlay-Projekte denselben Dateiknoten behalten. |

## Reproduzierbare Prüfungen

Aus dem Repository:

```powershell
dotnet run --project build/ParityFixtures -- tauri-app/src-tauri/crates/ccs-core/tests/fixtures
npm --prefix tauri-app run contracts:generate
npm --prefix tauri-app test
npm --prefix tauri-app run build
cargo test --manifest-path tauri-app/src-tauri/Cargo.toml --workspace
```

Die C#-Fixtures enthalten ausschließlich erzeugte Testdaten. Sie sind keine Kopie einer Benutzerinstallation. Vorhandene Benutzerdateien und Zugangsdaten werden durch die Tests nicht verwendet.

## Lokales Prüfergebnis

Am 6. September 2026 erfolgreich: 173 Rust-Tests, 56 Frontend-Tests (einschließlich der zusätzlichen MSI-Versionsregression), generierte Command-Verträge, TypeScript-Typprüfung und Vite-Produktionsbuild. Nach den letzten HTTP-Vertragskorrekturen wurde die gesamte Overlay-Crate erneut erfolgreich geprüft. Der globale Rust-Formatcheck meldet zusätzlich bereits vorhandene Formatabweichungen in `ccs-core/src/updates/`; die geänderten Rust-Dateien sind formatiert.

Windows-Release-Build erfolgreich: `npm run tauri build -- --bundles nsis,msi` erzeugt beide Installer unter `tauri-app/src-tauri/target/release/bundle/`. Der erste Versuch deckte die mit MSI inkompatible SemVer-Prereleasekennung auf; eine explizite numerische WiX-Version und deren Versionssynchronisierung beheben dies. Der Synchronisierungstest mit temporären Dateien erhält bestehende WiX-Optionen. Die Pakete wurden hier nicht installiert; macOS-Build und Betriebsabnahme sind nicht lokal nachgewiesen.

## Abgrenzung zu den Feature-Paketen

B7 stellt den Datenvertrag und die laufenden Messwerte bereit. Speicherung und Auswertung historischer Stream-Sessions bleiben DA3/DA4, der Ziele-Editor TW5. Drittanbieter-Emotes/Badge-Kataloge bleiben O6. Autostart, Tray und TitleBar-Karten sind als nicht verfügbar gekennzeichnet und bleiben SYS7. Allgemeine Datenmigration und Profil-/Backup-Verwaltung bleiben SYS3/SYS4. Passive C#-Einstellungen werden erhalten, ohne entfallene Module wieder zu aktivieren.

## Noch offene Betriebsabnahme

- Installierte Windows- und macOS-Pakete auf Systemen ohne Repository starten; Editor, Standalone- und Solo-Overlays in OBS laden.
- Echte OBS-, Twitch- und Musikverbindungen inklusive erneuter OAuth-Anmeldung herstellen.
- Portkonflikt, Dienstabbruch und Wiederverbindung während eines Streams prüfen; Musik, Chat und Alerts beobachten.
- Stream beenden, App neu starten und die übernommenen Daten prüfen.

Diese Schritte sind keine durch Mock- oder lokale Vertragstests ersetzte Abnahme. Bis zu ihrem Nachweis bleiben die Abnahme-Checkboxen offen und WPF verfügbar.
