# Overlay-System

Die Suite schreibt atomar nach:

`<DataRoot>\data\overlay-data.json`

Bereiche: stream, twitch, spotify/music, obs, alerts, stats und branding.

Eingebaute Overlays laufen über den lokalen Webserver: **Canvas** (`/view/{id}`), **Solo-Widgets** (`/w/…`), **Chat** (`/chat`). Externe HTML-Ordner unter `/o/{id}/` entfallen.

Details zum Editor: [`OVERLAY-EDITOR.md`](OVERLAY-EDITOR.md).

## Overlay-Webserver

Die Suite startet einen lokalen HTTP-Server auf `127.0.0.1` (Standard-Port **8765**).

- Canvas-Editor: `/editor/{id}` · View: `/view/{id}` (z. B. `/view/default`, `/view/just-chatting`)
- Solo-Widgets: `/w/{type}` · Solo-Shapes: `/w/shape/{shapeId}`
- Layout-API: `GET/PUT /layout/{id}` (PUT nur von Loopback)
- Eingebautes Chat-Overlay: `/chat`
- Daten: `GET /data/overlay-data.json`
- Optional: `GET /data/overlay-config.json`
- Live-Events: WebSocket `ws://127.0.0.1:8765/ws`
- Health: `GET /health` (inkl. Liste aller Canvases)

Layout-Dateien: `%LocalAppData%\CreatorControlSuite\Overlay\layouts\{id}.json`

Optionaler `RootPath` steuert nur noch den Datenroot für `overlay-data.json` (leer = LocalAppData).

## Mehrere benannte Canvases

Jedes Canvas hat eine stabile **Id** (URL/Datei) und einen **Anzeigenamen**. Live-Daten (`overlay-data.json`) sind für alle Canvases gemeinsam; nur das Layout unterscheidet sich.

In OBS: pro Design eine eigene Browserquelle auf `/view/{id}` (z. B. eine Quelle pro Szene). Die App wechselt die View-URL nicht live.

Verwaltung in der Overlay-Seite: auswählen, neu, umbenennen, duplizieren, löschen. Umbenennen ändert nur den Anzeigenamen; die Id und damit die OBS-URL bleiben gleich.

## Chat-Overlay

OBS-Browserquelle auf `/chat` (Standalone) oder Canvas-Widget `chat` / Solo `/w/chat`. Der Client verbindet sich auf `/ws` und rendert Events vom Typ `channel.chat.message`.

Session-History: Nachrichten werden serverseitig gepuffert und bei Connect sowie über `GET /chat/history` zurückgespielt.

Appearance (Hintergrund, Padding, Radius, Gap, Schriftgröße, Schriftart): `/chat/config` bzw. Props am Canvas-Widget.

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
| `twitch` | `channel.chat.message` | Chat inkl. Emote-`parts` |
| `app` | `app.stream.phase` | Workflow-Phase |
| `app` | `app.stream.live` | Live start/end |
| `app` | `app.countdown` | Globaler Overlay-Countdown (`isRunning`, `remainingSeconds`, `endsAt`, `label`) |
| `app` | `app.obs.scene` | OBS-Szene |
| `app` | `app.music.track` | Now Playing (Spotify/YT Music) |
| `app` | `app.alert` | Alert gestartet |
| `app` | `app.overlay.layout` | Canvas-Layout gespeichert (`instanceId`) |
| `app` | `app.ws.hello` | Willkommen beim Connect |
| `editor` | `editor.layout.set` | Client→Server Layout speichern |

## Einstellungen

| Einstellung | Bedeutung |
|-------------|-----------|
| `WebServerEnabled` | Overlay-Webserver starten |
| `Port` / `WebServerPort` | HTTP-Port (Standard 8765) |
| `RootPath` | Optionaler Datenroot (leer = LocalAppData) |
| `Canvases` | Liste `{ Id, Name }` der Overlay-Canvases |
| `SelectedCanvasId` | In der Overlay-UI ausgewähltes Canvas |
| `Chat.*` | Chat-Overlay-Einstellungen |

Konfiguration: Overlay-Seite → Webserver, Chat, Canvas-Liste / URLs / Editor / Widgets.
