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
- Optionaler Browser-Dev-Server (ohne WPF, Node 18+): `CanvasOverlay/` → `npm run dev` bzw. `make canvas-dev` — simuliert denselben Overlay-Webserver inkl. Hot-Reload.

Layout-Dateien: `%LocalAppData%\CreatorControlSuite\Overlay\layouts\{id}.json`

Optionaler `RootPath` steuert nur noch den Datenroot für `overlay-data.json` (leer = LocalAppData).

## Mehrere benannte Canvases

Jedes Canvas hat eine stabile **Id** (URL/Datei) und einen **Anzeigenamen**. Live-Daten (`overlay-data.json`) sind für alle Canvases gemeinsam; nur das Layout unterscheidet sich.

In OBS: pro Design eine eigene Browserquelle auf `/view/{id}` (z. B. eine Quelle pro Szene). Die App wechselt die View-URL nicht live.

Verwaltung in der Overlay-Seite: auswählen, neu, umbenennen, duplizieren, löschen. Umbenennen ändert nur den Anzeigenamen; die Id und damit die OBS-URL bleiben gleich.

## Chat-Overlay

OBS-Browserquelle auf `/chat` (Standalone) oder Canvas-Widget `chat` / Solo `/w/chat`. Der Client verbindet sich auf `/ws` und rendert Events vom Typ `channel.chat.message`.

Session-History: Nachrichten werden serverseitig gepuffert (Kapazität `maxLines × 2` über Canvas-Chat-Widgets), bei Connect sowie über `GET /chat/history` zurückgespielt und persistent unter `%LocalAppData%\CreatorControlSuite\Overlay\chat-history.json` gespeichert (debounced + Flush beim App-Ende).

Moderation: `/clear` von Broadcaster/Mod leert Hub, Datei und Clients (`app.chat.clear`). Twitch-EventSub `channel.chat.message_delete`, `channel.chat.clear` und `channel.chat.clear_user_messages` entfernen Zeilen im Overlay.

Appearance: `/chat/config` bzw. umfangreiche Props am Canvas-Widget (Varianten, Typografie, Farben, Badges/Emotes/Timestamps, …).

## Extension Packs

ZIP-Pakete mit zusätzlichen Widgets, Effekten, Animationen und Schriften, ohne die App-Assemblies neu zu bauen.

- Speicherort: `%LocalAppData%\CreatorControlSuite\Overlay\extensions\{packId}\`
- Katalog: `GET /extensions` → `{ packs: [...] }` (aus jeder installierten `manifest.json`)
- Dateien: `GET /ext/{packId}/{*path}` (nur Dateien innerhalb des Pack-Ordners, `no-store`)
- Verwaltung (nur Loopback): `POST /extensions/install` (multipart ZIP), `DELETE /extensions/{packId}`
- Manifest-Schema (`apiVersion: 1`): `id` (Slug `[a-z0-9-]+`), `name`, `version`, `apiVersion`, `widgets[]`, `effects[]`, optional `animations[]`, `fonts[]`, optional `assets[]`
- Installation validiert Manifest, erlaubte Dateitypen (`.js,.css,.woff2,.woff,.ttf,.otf,.svg,.png,.jpg,.jpeg,.webp,.gif,.json,.md`), Zip-Slip-Schutz und eine Größenobergrenze (50 MB)
- Runtime (`/view`, `/w/…`, Editor-Canvas): `loadExtensions()` läuft **vor** dem ersten `setLayout`/`renderItems`; Pack-CSS/-JS per `fetch` + Inline-Inject (OBS-CEF feuert oft kein `<link>`/`<script src>`-`onload`); Pack-`update`-Hooks bei Data-Refresh
- Editor: Pack-Widgets erscheinen nach Boot automatisch in der linken Palette (`Extension · {Pack}`); Effekte und Animationen nach `registerEffect`/`registerAnimation` in den Inspector-Dropdowns

Verwaltung in der Overlay-Seite: Karte „Extension Packs“ (ZIP importieren, deinstallieren). Implementierung: `IOverlayExtensionStore` / `OverlayExtensionStore` in `CreatorControlSuite.Modules.Overlay/Extensions/`.

## Asset-Bibliothek

Importierte Bilder für Overlay und App-UI (kopiert, nicht nur referenziert).

- Speicherort: `%LocalAppData%\CreatorControlSuite\Overlay\assets\` (+ `index.json`)
- Katalog: `GET /assets` → `{ assets: [{ id, name, url, contentType, size, createdAt }] }`
- Datei: `GET /assets/{id}` (Content-Type aus Extension; `no-store`)
- Verwaltung (nur Loopback): `POST /assets` (multipart Bild), `DELETE /assets/{id}`
- Allowlist: `.png,.jpg,.jpeg,.webp,.gif,.bmp,.svg` · Größenlimit 15 MB
- Overlay-Layout speichert relative URLs `/assets/{id}`; WPF-Picker (Chat-Hintergrund, Dashboard-Icons) speichern den lokalen Kopie-Pfad
- Implementierung: `IOverlayAssetStore` / `OverlayAssetStore` in `CreatorControlSuite.Modules.Overlay/Assets/`

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

## Built-in Effects (`item.effects[]`)

Stapelbare Modifier auf allen Layout-Items. Registry: `EFFECT_STRATEGIES` / Skill `overlay-effect`.

Jedes Effect-Objekt kann `target` setzen (nur wenn die Strategy es unterstützt):

| target | Bedeutung |
|--------|-----------|
| `box` (Default) | Effekt auf die Item-Box / den Container |
| `content` | Effekt auf das gezeichnete Element (`[data-role=content]`, z. B. Kreis/Text); Glow/Drop-Shadow nutzen `filter: drop-shadow` (Silhouette) |

`content` ist freigeschaltet für: `glow`, `drop-shadow`, `outline`, `glitch`. Andere Built-ins sind **nur Box** (`strategy.targets`, Default `["box"]`).

| type | Label | Kurzbeschreibung |
|------|-------|------------------|
| `glow` | Glow | Soft glow; Animation `pulse`/`breathe` (Inhalt: Silhouette bleibt, Box: Layer) |
| `particles` | Particles | Partikel-Modi (ember, snow, …) |
| `scanlines` | Scanlines | CRT-Linien |
| `vignette` | Vignette | Abgedunkelte Ränder |
| `blur` | Blur | Backdrop-Blur |
| `noise` | Noise | Filmgrain |
| `neon` | Neon | Pulsierender Neon-Rahmen |
| `glitch` | Glitch | RGB-Split / Jitter |
| `sparkle` | Sparkle | Funkeln (Subs/Hype) |
| `aurora` | Aurora | Fließendes Nordlicht |
| `pulse-ring` | Pulse Ring | Expandierende Alert-Ringe |
| `hologram` | Hologram | Holo-Scan-Shimmer |
| `outline` | Outline | Harter Contour-Stroke; optional `pulse`/`breathe` |
| `drop-shadow` | Drop Shadow | Gerichteter Schatten; optional `pulse`/`breathe` |
| `rainbow` | Rainbow | Animierter Regenbogen-Rand |
| `spotlight` | Spotlight | Wandernder Lichtkegel |

## Built-in Animations (`item.animations[]`)

Motion auf dem Item-Content. Registry: `ANIMATION_STRATEGIES`.

| type | Label | Kurzbeschreibung |
|------|-------|------------------|
| `fade` | Fade | Opacity-Pulse |
| `slide` | Slide | Verschieben (Richtung) |
| `bounce` | Bounce | Auf-und-ab |
| `pop` | Pop | Scale-Pop |
| `shake` | Shake | Rütteln (Attention) |
| `float` | Float | Sanftes Schweben |
| `pulse` | Pulse | Herzschlag-Scale |
| `spin` | Spin | Rotation |
| `wiggle` | Wiggle | Wackeln |
| `flip` | Flip | 3D-Flip (X/Y) |

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
