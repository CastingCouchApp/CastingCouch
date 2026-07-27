# Overlay-System

Die Suite schreibt atomar nach:

`<DataRoot>\data\overlay-data.json`

Bereiche: stream, twitch, spotify, obs, alerts, stats und branding.

Overlay-HTML und Assets liegen in **Overlay-Instanzen** — jede mit eigenem Root-Ordner. Die Suite liefert Dateien und JSON über einen lokalen Webserver aus; OBS-Browserquellen verweisen auf URLs unter `/o/{id}/`.

## Overlay-Instanzen

In den Overlay-Einstellungen können mehrere Instanzen angelegt werden (`Id`, `Name`, `RootPath`, `Enabled`).

- Statische Dateien: `http://127.0.0.1:8765/o/{id}/…`
- Legacy-`RootPath` ohne Instanzen wird beim Laden als Instanz „Default“ migriert
- Die zentrale Daten-JSON bleibt **eine** Datei (nicht pro Instanz)

## Overlay-Webserver

Die Suite startet einen lokalen HTTP-Server auf `127.0.0.1` (Standard-Port **8765**).

- Statische Dateien pro Instanz: `http://127.0.0.1:8765/o/{id}/meine-szene.html`
- Eingebautes Chat-Overlay: `http://127.0.0.1:8765/chat`
- Daten: `GET /data/overlay-data.json`
- Optional, falls vorhanden: `GET /data/overlay-config.json`
- Live-Events: WebSocket `ws://127.0.0.1:8765/ws` (nur getypte Events, kein JSON-Snapshot)
- Health: `GET /health` (inkl. `overlays` und `clients`)

Die JSON-Datei bleibt Source of Truth für HTTP-Polling. Der WebSocket pusht **keine** Full-Snapshots mehr.

## Chat-Overlay

OBS-Browserquelle auf `/chat`. Der Client verbindet sich auf `/ws` und rendert Events vom Typ `channel.chat.message`.

- Twitch-Emotes aus EventSub-`fragments`
- BTTV / FFZ / 7TV optional über Overlay-Einstellungen (`Overlay.Chat`)
- Beim Connect werden die letzten gepufferten Chat-Events erneut gesendet

## WebSocket-Event-Schema

Jedes Frame ist ein JSON-Objekt:

```json
{
  "source": "twitch",
  "type": "channel.follow",
  "at": "2026-07-27T18:00:00+00:00",
  "summary": "alice folgt jetzt",
  "data": { "user_name": "alice" }
}
```

| source | type (Beispiele) | Bedeutung |
|--------|------------------|-----------|
| `twitch` | EventSub-Typ (`channel.follow`, …) | Twitch EventSub |
| `twitch` | `channel.chat.message` | Chat inkl. Emote-`parts` (JSON-String in `data.parts`) |
| `app` | `app.stream.phase` | Workflow-Phase |
| `app` | `app.stream.live` | Live start/end (`data.isLive`) |
| `app` | `app.obs.scene` | OBS-Szene |
| `app` | `app.spotify.track` | Spotify-Titel |
| `app` | `app.alert` | Alert gestartet |
| `app` | `app.ws.hello` | Willkommen beim Connect |

Chat-Payload (`data`):

| Feld | Inhalt |
|------|--------|
| `messageId` | Twitch Message-ID |
| `userName` / `userLogin` | Anzeigename / Login |
| `color` | Chat-Farbe (`#RRGGBB`) |
| `badges` | JSON-Array: `{ setId, id, url?, title? }` (Badge-Icons) |
| `parts` | JSON-Array: `{ type, text, url?, provider? }` (`text`/`emote`) |

Chat-Config: `GET /chat/config` (`showTwitchEvents`, Emote-Flags).

Bei `showTwitchEvents` blendet `/chat` zusätzlich Follow/Sub/Raid/Cheer/… aus denselben `/ws`-Events ein.

Alle verbundenen Overlay-Clients empfangen dieselben Events (Fan-out).

## Einstellungen

| Einstellung | Bedeutung |
|-------------|-----------|
| `WebServerEnabled` | Overlay-Webserver starten |
| `Port` / `WebServerPort` | HTTP-Port (Standard 8765) |
| `Instances` | Liste der Overlay-Ordner |
| `RootPath` | Legacy / Daten-Fallback (wird aus erster Instanz synchronisiert) |
| `Chat.Enabled` | Chat-Overlay `/chat` und Chat-Publishes |
| `Chat.ShowTwitchEvents` | Follow/Sub/Raid/… im Chat-Overlay |
| `Chat.BackgroundType` | `None` / `Color` / `Image` |
| `Chat.BackgroundColor` | Hintergrundfarbe (`#RRGGBB`) |
| `Chat.BackgroundImagePath` | Lokaler Bildpfad |
| `Chat.BackgroundOpacity` | 0–1 Transparenz nur der Hintergrundschicht |
| `Chat.PaddingPx` / `BorderRadiusPx` / `GapPx` | Abstand, Ecken, Zeilenabstand |
| `Chat.EnableBttv` / `EnableFfz` / `EnableSevenTv` | Third-Party-Emotes |
| `Chat.MaxBufferedMessages` | Ringpuffer für WS-Replay |

Konfiguration: Overlay-Seite → „Overlay-Webserver“, „Chat-Overlay“ und „Overlay-Instanzen“ bzw. Ersteinrichtung → Overlay-Ordner.
