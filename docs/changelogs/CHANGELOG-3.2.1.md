# Creator Control Suite 3.2.1

## Raid-Countdown und konfigurierbares Streamende

- Sichtbarer Raid-Countdown im Dashboard mit Raid-Ziel, aktueller Zuschauerzahl und Fortschrittsbalken.
- Der Countdown läuft nach erfolgreichem Start des Twitch-Raids rückwärts.
- Ein aktiver Raid kann über „RAID ABBRECHEN“ beendet werden; der OBS-Stream bleibt dann aktiv.
- Der Countdown ist unter Dienste > Twitch konfigurierbar (Standard: 90 Sekunden).
- Optionales automatisches Beenden des OBS-Streams nach dem Raid.
- Optionales Pausieren von Spotify nach dem Raid.
- Der bestehende Streamer.bot-Chat-Hinweis bleibt unverändert; es wurde bewusst keine zusätzliche Overlay-Meldung ergänzt.

Hinweis: Twitch bietet über die verwendete API keinen Befehl zum sofortigen Überspringen des Raid-Countdowns. Daher wurde kein irreführender „Raid sofort“-Button eingebaut.
