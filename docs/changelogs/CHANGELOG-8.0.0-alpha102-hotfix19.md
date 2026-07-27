# 8.0.0-alpha102-hotfix19

## Resilientes Streamende mit Raid

- Nach der Endszene startet der Raid automatisch (Status-Polling + StartRaid-Retry) statt unbegrenzt auf einen manuellen Klick zu warten.
- Neues Setting **Auto-Raid Timeout** (Standard 120 s) unter Dienste → Twitch → Streamende und Raid; danach wird der Stream ohne Raid beendet.
- Manueller Early-Start, Skip und Abbruch bleiben verfügbar.
- OBS-Stream-Stop prüft die Verbindung, versucht Reconnect und wiederholt den Stop bis zu 3×; Startszene-Fehler blockieren das Ende nicht.
- Abort während aktivem Raid-Countdown bricht den Twitch-Raid best-effort ab.
- Helix-Raid-Fehler (400/401/403/409/429/5xx) erscheinen als kurze deutsche Meldungen.
