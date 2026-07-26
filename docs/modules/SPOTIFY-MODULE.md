# Spotify-Modul – 2.0.81

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

Wichtiger Produkt- und Nutzungshinweis:

- Spotify-Inhalte dürfen nicht über den Livestream ausgestrahlt werden.
- Die Spotify Platform darf laut aktueller Spotify-Richtlinie nicht für
  kommerzielle Streaming-Integrationen verwendet werden.
- Vor einem Verkauf muss geklärt werden, ob und in welchem Umfang das
  Spotify-Modul in einem kommerziellen Produkt angeboten werden darf.
- Eine mögliche Alternative ist ein allgemeines Medienplayer-Modul, das
  lokale oder lizenzierte Musik steuert.
