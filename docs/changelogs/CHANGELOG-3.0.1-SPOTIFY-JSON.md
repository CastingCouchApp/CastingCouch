# Creator Control Suite 3.0.1 – Spotify-JSON-Fix

Diese Zwischenversion ändert ausschließlich das Schreiben der Spotify-Laufzeitdaten.

## Geändert

- Spotify schreibt direkt in den bereits gespeicherten Pfad `data/overlay-data.json`.
- Während des Spotify-Pollings werden keine Einstellungen gespeichert.
- Das sichtbare Pfadfeld überschreibt die gespeicherte Konfiguration nicht mehr automatisch.
- Vorhandene unbekannte JSON-Felder bleiben erhalten.
- Der Spotify-Bereich zeigt nach erfolgreichem Schreiben `Spotify-Daten geschrieben: HH:mm:ss` an.
- Schreibvorgänge werden serialisiert, damit parallele Spotify-Aktualisierungen die Datei nicht gleichzeitig verändern.

## Nicht geändert

- Alert-Ducking
- Metaschutz-Mikrofonstatus
- OBS-Automatisierung
- Start-/Game-Countdown
