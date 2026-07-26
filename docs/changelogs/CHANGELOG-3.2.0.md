# Creator Control Suite 3.2.0

## Stabilität bei OBS

- `RefreshObsAsync()` beendet die Anwendung nicht mehr, wenn OBS beim Öffnen eines Dropdowns oder beim manuellen Aktualisieren noch nicht verbunden ist.
- Der nicht verbundene Zustand wird jetzt im Dashboard und unter Dienste > OBS angezeigt.
- Auch ein Verbindungsabbruch während einer laufenden OBS-Abfrage wird abgefangen.
- UI-Statusänderungen werden sicher über den WPF-Dispatcher ausgeführt.

## Enthalten aus 3.1.2

- Spotify-Lautstärkeregelung für Workflow-Szenen und Streamer.bot-Alerts.
- Automatisches Schreiben der Spotify-Daten in die konfigurierte JSON-Datei.
- Persistente zeitgesteuerte OBS-Aktionen mit Bearbeiten und Löschen.
- Streamer.bot-Actions-Anzeige.
- Dunklere, lesbare Tabs, Listen und Statistikflächen.
