# Creator Control Suite 8.0.0-alpha102 Hotfix 15

- Live-Status wird gegen kurzzeitige leere OBS-Output-Abfragen entprellt; Uptime und Startzeit bleiben stabil.
- Spotify-Automatik und Anzeigeoptionen speichern nicht mehr während des initialen UI-Ladevorgangs.
- Startplaylist-, Streamstart-, Endmusik-, Alert- und Live-Lautstärkeoptionen werden unmittelbar gespeichert.
- Vor dem Streamstart werden die aktuellen Spotify-Automatikfelder nochmals übernommen und persistiert.
- Veraltete Pause-Regeln für die konfigurierte Game-Szene werden übersprungen, wenn nur eine Lautstärkeänderung eingestellt ist.
- Beim Wechsel Start → Game bleibt eine zuvor laufende Wiedergabe aktiv und die konfigurierte Lautstärke wird gesetzt.
- Doppelte SetVolume-Verzweigung in der Szenenautomation entfernt.
