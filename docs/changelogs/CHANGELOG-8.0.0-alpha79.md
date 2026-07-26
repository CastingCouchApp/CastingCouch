# Creator Control Suite 8.0.0-alpha79

## Spotify-Verfuegbarkeit und Overlay-Stabilitaet

- Spotify-Bereiche werden beim Aktualisieren unabhaengig geladen. Ein Fehler bei Queue, Verlauf oder Player blockiert nicht mehr Geraete und Playlists.
- Bei abgelaufenen Zugriffstokens wird jeder Bereich einmal mit erneuertem Token wiederholt.
- Scope-Informationen bleiben bei Spotify-Token-Erneuerungen erhalten, wenn Spotify keine neue Scope-Liste mitsendet.
- Kurze leere Player-Antworten beim Titelwechsel werden fuer das Overlay drei Sekunden abgefedert.
- Die OBS-Spotify-Quelle wird bei kurzen Spotify-Zustandswechseln nicht mehr sofort aus- und wieder eingeblendet.
- Der letzte gueltige Titel bleibt waehrend eines kurzen Spotify-Polling-Aussetzers sichtbar.
