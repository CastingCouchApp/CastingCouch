# Music Player – Spotify & YouTube Music

Die Suite unterstützt genau **einen** aktiven Music-Player gleichzeitig.

## Provider-Wahl

Unter **Einstellungen → Music Player**:

- Radio-Auswahl: **Spotify** | **YouTube Music**
- Je nach Auswahl erscheinen die passenden Verbindungsoptionen

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
3. Bookmarklet per Drag-and-Drop in die Lesezeichenleiste ziehen:
   - orangene Kachel in der App ziehen, oder
   - **Install-Seite öffnen** (`http://127.0.0.1:{port}/ytmusic/install`) und den Link dort ziehen
4. [music.youtube.com](https://music.youtube.com) öffnen, Lesezeichen einmal ausführen und Tab offen lassen

Das Bookmarklet enthält den Bridge-Code **inline** (kein externes `script.src`), weil YouTube Music Trusted Types (`require-trusted-types-for 'script'`) Script-Injection blockiert. Auto-Reconnect (Health-Check, Backoff, erneuter Klick = Restart, Tab-Focus/`online`) ist eingebaut. Nach Bridge-Updates das Lesezeichen neu ziehen.

Die Bridge lauscht auf `http://127.0.0.1:{BridgePort}/ytmusic/`:

- `POST /state` – Titel, Artist, Album, Cover, Playing, Progress
- Cover: Media Session + Player-Bar/Player-Page/Video-Poster; Album aus Byline (`Artist • Album`) / Album-Link / Media Session
- `GET /commands` – pending Controls
- `GET /bookmarklet.js` – Bridge-Script
- `GET /install` – HTML-Seite mit ziehbarem Bookmarklet-Link
- `GET /health` – Alive-Check für Auto-Reconnect

## Overlay

Weiterhin Key `spotify` in `overlay-data.json` (Kompatibilität mit DenverJohn). Zusätzlich Key `music` (Spiegel). Feld `provider` / `providerDisplayName` (`spotify` | `ytmusic`).

Canvas-Widget-Typ: **`music`** (Solo-URL `/w/music`). Legacy-Typ/`/w/spotify` bleibt Alias.

WebSocket: `app.music.track` (mit `provider`).
