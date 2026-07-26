# Creator Control Suite 8.0.0-alpha102 Hotfix 12

## Spotify-Overlay: kein Reset mehr bei einzelnen leeren Polls

- `overlay-data-client.js` bleibt ausschließlich der Leser der JSON und wird nicht als Datenspeicher verwendet.
- Der zentrale Spotify-JSON-Schreiber stabilisiert nun auch Daten aus direkten Aufrufen.
- Ein kurzzeitig leerer Spotify-Snapshot überschreibt `Overlay/data/overlay-data.json` nicht mehr mit `connected=false`, leeren Titeln und leerem Cover.
- Der letzte gültige Spotify-Titel bleibt erhalten, solange die Spotify-Verbindung zuvor erfolgreich hergestellt wurde.
- Nur das ausdrückliche Trennen über die Suite leert den Spotify-Abschnitt und setzt `connected=false`.
