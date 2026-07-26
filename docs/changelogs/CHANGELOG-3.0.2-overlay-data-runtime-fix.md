# Creator Control Suite 3.0.2 – Overlay-Daten Laufzeit-Fix

- basiert auf der funktionierenden Startup-Crash-Fix-Version
- Spotify-Polling speichert keine Programmeinstellungen mehr
- die konfigurierte vorhandene `overlay-data.json` wird direkt aktualisiert
- Fallback auf `<OverlayRoot>\data\overlay-data.json`
- sichtbarer Zeitstempel nach erfolgreichem Schreiben
- robustes Überschreiben, falls Windows das atomare Umbenennen kurz blockiert
- keine Änderungen an Alert-Ducking, Metaschutz oder Startautomatisierung
