# Creator Control Suite 8.0.0-alpha102 Hotfix 6

## Spotify-Overlay-Flackern behoben

- Nur noch die zentrale Sichtbarkeitsroutine schreibt `spotify.showInOverlay` und `spotify.visible`.
- Die normale Spotify-Datenaktualisierung überschreibt den Sichtbarkeitszustand nicht mehr bei jedem Poll.
- Sichtbarkeitsprüfungen werden serialisiert, damit ältere asynchrone Ergebnisse keine neueren Zustände überschreiben.
- Bei kurzfristigen OBS-Abfragefehlern bleibt der zuletzt sicher erkannte Mute-Zustand erhalten.
- Die Einstellung „bei Pause ausblenden“ wird konsistent in die Overlay-JSON geschrieben.
