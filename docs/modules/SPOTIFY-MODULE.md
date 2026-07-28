# Spotify-Modul

## OAuth

Die Desktop-Anwendung verwendet Authorization Code mit PKCE.

- kein Client-Secret in der Anwendung
- Loopback Redirect über `127.0.0.1`
- OAuth-State-Prüfung
- SHA-256 Code Challenge
- Access- und Refresh-Token via Windows DPAPI
- automatische Token-Aktualisierung

## Web API

Implementiert:

- Benutzerprofil
- verfügbare Geräte
- Playback State
- private Playlists
- Wiedergabe auf Gerät übertragen
- Playlist starten
- Resume
- Pause
- vorheriger/nächster Titel
- Lautstärke
- programmatischer Fade-In/Fade-Out
- Bibliothek speichern, entfernen und prüfen

Der HTTP-Client verwendet die seit März 2026 geltenden Development-Mode-
Verträge:

- `PUT`/`DELETE /me/library` und `GET /me/library/contains` mit Spotify-URIs
- `/playlists/{id}/items` und das umbenannte `items`-Antwortfeld
- maximal zehn Suchergebnisse pro Anfrage
- nullable Playback-Felder sowie unbekannte zusätzliche JSON-Felder

Aufgezeichnete JSON-Fixtures prüfen Playback, Nullable-Felder, Playlist-Paging,
Bibliotheksrouten, Bearer-Authentifizierung und URL-Encoding. Die alten
`tracks`-Felder werden beim Lesen weiterhin akzeptiert, damit Extended-Quota-
Antworten kompatibel bleiben.

## Oberfläche

- Spotify autorisieren
- verbinden/trennen
- Geräteauswahl
- Playlist-Auswahl
- Play/Pause/Zurück/Weiter
- Lautstärkeregler
- aktueller Titel
- Verbunden · Spielt
- Verbunden · Pause
- Fade-Out-Test
- Startplaylist

## Voraussetzungen und Einschränkungen

Viele Spotify-Player-Endpunkte erfordern Spotify Premium.
Development-Mode-Apps unterliegen zusätzlich den jeweils aktuellen Konto-,
Nutzer- und Endpunktgrenzen. Die technische Grundlage ist der
[Spotify-Migrationsleitfaden für Februar 2026](https://developer.spotify.com/documentation/web-api/tutorials/february-2026-migration-guide).

Wichtiger Produkt- und Nutzungshinweis:

- Spotify-Inhalte dürfen nicht über den Livestream ausgestrahlt werden.
- Die Spotify Platform darf laut aktueller Spotify-Richtlinie nicht für
  kommerzielle Streaming-Integrationen verwendet werden.
- Vor einem Verkauf muss geklärt werden, ob und in welchem Umfang das
  Spotify-Modul in einem kommerziellen Produkt angeboten werden darf.
- Albumcover und Metadaten benötigen die von Spotify geforderte Attribution;
  die bestehende Overlay-Darstellung ist vor Freigabe gesondert zu prüfen.
- Eine mögliche Alternative ist ein allgemeines Medienplayer-Modul, das
  lokale oder lizenzierte Musik steuert.
