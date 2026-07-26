# 8.0.0-alpha102-hotfix17

- Stream-Laufzeitdaten besitzen jetzt einen einzigen Schreiber: die bestätigte OBS-Streamüberwachung.
- Der allgemeine OverlayDataService übernimmt den Bereich `stream` nicht mehr und kann `isLive`/`startedAt` daher nicht zurücksetzen.
- Nicht erreichbare Remote-OBS-Instanzen gelten nicht mehr als bestätigtes Streamende.
- Offline wird erst nach 15 verbundenen, aufeinanderfolgenden OBS-Inaktiv-Abfragen gesetzt.
- Der Spotify-Playliststart liest ausschließlich die dauerhaft gespeicherten Einstellungen statt noch nicht vollständig geladener UI-Felder.
- Die Startplaylist wird zentral beim bestätigten OBS-Übergang auf LIVE ausgelöst, unabhängig vom verwendeten Startweg.
- Spotify erhält bei einem kurzfristig fehlenden Player/Gerät genau einen verzögerten Wiederholungsversuch.
