# Creator Control Suite 8.0.0-alpha81

## Spotify-Ausbau – Phase 1

- Spotify-Teilbereiche melden API-Fehler jetzt sichtbar statt sie still zu verschlucken.
- Gerätebereich erklärt den Unterschied zwischen fehlender Berechtigung und keinem aktiven Spotify-Gerät.
- Playlistbereich zeigt Authentifizierungs-, Berechtigungs- und Rate-Limit-Fehler direkt an.
- Alle eigenen und kollaborativen Playlists werden seitenweise geladen, nicht mehr nur die ersten 50.
- Playlisttitel werden seitenweise bis zu 500 Titel geladen.
- Neue Autorisierungen fordern zusätzlich `playlist-read-collaborative` an.
- Playlists werden alphabetisch sortiert und Dubletten entfernt.
