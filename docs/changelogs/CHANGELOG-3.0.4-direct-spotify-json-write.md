# Creator Control Suite 3.0.4

- Spotify-Laufzeitdaten werden direkt in die vom DenverJohn-v18-Overlay geladene Datei `Overlay/data/overlay-data.json` geschrieben.
- Der allgemeine OverlayDataService wird für Spotify nicht mehr als Pfadvermittler verwendet.
- Gleichzeitige Spotify-Polling-Schreibvorgänge werden durch eine Sperre serialisiert.
- Bestehende unbekannte JSON-Felder bleiben erhalten.
- Der funktionierende Startup-Crash-Fix bleibt unverändert enthalten.
