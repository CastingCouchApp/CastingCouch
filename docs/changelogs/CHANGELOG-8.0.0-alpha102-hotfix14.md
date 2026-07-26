# Creator Control Suite 8.0.0-alpha102 Hotfix 14

## Spotify-Overlay: allgemeiner JSON-Schreiber entkoppelt

- Der allgemeine `OverlayDataService` überschreibt den vorhandenen `spotify`-Unterbaum nicht mehr.
- OBS-, Twitch-, Szenen- und Browserquellen-Aktualisierungen können dadurch keinen Standardwert `connected=false` mehr in die aktive `overlay-data.json` schreiben.
- Ausschließlich der dedizierte Spotify-Laufzeitschreiber aktualisiert Spotify-Verbindung, Titel, Interpret, Cover, Fortschritt und Sichtbarkeit.
- Bestehende Spotify-Daten bleiben bei allen anderen Overlay-Aktualisierungen vollständig erhalten.
