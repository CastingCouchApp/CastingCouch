# Creator Control Suite 3.0.9

## Streamer.bot-Alerts und Spotify-Ducking

- Neue IPC-Befehle `alert.external.start`, `alert.external.end` und `alert.external.clear`.
- Streamer.bot-Alerts verwenden dieselbe Spotify-Absenkung wie Suite-Alerts.
- Mehrere gleichzeitig aktive Alerts werden gezählt.
- Die ursprüngliche Spotify-Lautstärke wird erst wiederhergestellt, wenn alle Suite- und Streamer.bot-Alerts beendet sind.
- Einrichtungsanleitung in `STREAMERBOT-ALERT-DUCKING.md` ergänzt.

## Overlay-Import

- Fehlt beim Import eine nutzbare `overlay-data.json`, erzeugt die Suite im `data`-Verzeichnis des Overlay-Projekts automatisch eine neue Datei.
- Falls Windows keinen Hardlink oder symbolischen Link zulässt, wird als zuverlässiger Fallback eine lokale Datei angelegt, statt den Import abzubrechen.
