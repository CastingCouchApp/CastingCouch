# Creator Control Suite 8.0.0 Alpha 102 – Hotfix 10

## Spotify-Overlay dauerhaft sichtbar und stabil verbunden

- Spotify-Overlay-Verbindungsstatus wird nach erfolgreicher Verbindung gehalten.
- Kurzzeitige Poll- oder Token-Snapshots setzen `spotify.connected` nicht mehr auf `false`.
- `spotify.connected=false` wird nur noch bei ausdrücklichem Trennen geschrieben.
- `spotify.showInOverlay` und `spotify.visible` werden bei jedem Spotify-Datenupdate gesetzt.
- Fehlende oder veraltete Sichtbarkeitsfelder können die HTML nicht mehr dauerhaft ausblenden.
- Beim erneuten Verbinden wird der Sichtbarkeitscache zurückgesetzt.
