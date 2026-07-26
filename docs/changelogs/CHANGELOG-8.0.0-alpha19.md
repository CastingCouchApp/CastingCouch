# Creator Control Suite 8.0.0-alpha19

## Spotify-Shuffle im Dashboard

- Der bislang deaktivierte Shuffle-Button im Spotify-Dashboard ist jetzt funktionsfähig.
- Zufallswiedergabe kann für das aktuell aktive Spotify-Gerät direkt ein- und ausgeschaltet werden.
- Der tatsächliche `shuffle_state` wird aus der Spotify-Wiedergabeantwort übernommen.
- Der Button zeigt sichtbar an, wenn Shuffle aktiv ist, und aktualisiert seinen Hilfetext entsprechend.
- Abgelaufene Spotify-Zugriffstoken werden beim Umschalten wie bei anderen geschützten Spotify-Aktionen automatisch erneuert und die Aktion wird einmal wiederholt.
- Die bereits vorhandene Shuffle-Einstellung für die Start-Playlist bleibt unverändert erhalten.
