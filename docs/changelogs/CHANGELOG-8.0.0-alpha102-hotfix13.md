# Creator Control Suite 8.0.0-alpha102 Hotfix 13

## Spotify-Overlay: Entprellung kurzer API-Aussetzer

- Einzelne Netzwerk-, Token-, Rate-Limit- und API-Fehler überschreiben den letzten gültigen Spotify-Playerzustand nicht mehr.
- 401-Antworten erneuern zuerst das Token und wiederholen die Abfrage einmal.
- Leere Spotify-Playerantworten werden erst nach fünf aufeinanderfolgenden Treffern und mindestens 15 Sekunden ohne gültigen Titel übernommen.
- `spotify.connected=false` darf ausschließlich während eines ausdrücklich gestarteten Spotify-Disconnects geschrieben werden.
- Titel, Interpret, Cover und Fortschritt bleiben während kurzer Spotify-Aussetzer sichtbar.
