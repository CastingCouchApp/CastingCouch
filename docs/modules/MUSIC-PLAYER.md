# Music Player – Spotify & YouTube Music

Die Suite unterstützt genau **einen** aktiven Music-Player gleichzeitig.

## Provider-Wahl

Unter **Einstellungen → Allgemein → Music Player**:

- Spotify
- YouTube Music

Die Auswahl steuert Title-Bar-Widget, Player-Seite, Overlay-Schreiben und das Connection-Widget.

## Player-Seite

Sidebar **Player**:

- Now Playing (Titel, Artist, Cover, Progress)
- Play / Pause / Weiter / Zurück
- Verbinden / Trennen
- Bei Spotify: Lautstärke & Seek; Link zu Dienste → Spotify für Geräte/Playlists
- Bei YouTube Music: Bookmarklet-Setup und Bridge-Status

## YouTube Music Bridge

1. Provider auf YouTube Music stellen und speichern
2. Bridge starten (Verbinden auf der Player-Seite oder Auto-Connect)
3. [music.youtube.com](https://music.youtube.com) öffnen
4. Bookmarklet ausführen und den Tab offen lassen

Die Bridge lauscht auf `http://127.0.0.1:{BridgePort}/ytmusic/`:

- `POST /state` – Titel, Artist, Cover, Playing, Progress
- `GET /commands` – pending Controls
- `GET /bookmarklet.js` – Bridge-Script

## Overlay

Weiterhin Key `spotify` in `overlay-data.json` (Kompatibilität). Zusätzliches Feld `provider` (`spotify` | `ytmusic`).
