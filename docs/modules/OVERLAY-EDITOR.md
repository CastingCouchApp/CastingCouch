# Overlay Editor

Canvas-Editor: Widgets und Frames per Drag & Drop, Live-Push an OBS, Auto-Save.

Siehe auch: [`OVERLAY-SYSTEM.md`](OVERLAY-SYSTEM.md)

## URLs (Port Standard 8765)

| URL | Zweck |
|-----|--------|
| `/editor/{id}` | Editor für Canvas `id` (WebView in der App oder Browser) |
| `/view/{id}` | Volles Canvas-Layout als OBS-Browserquelle |
| `/w/online` | Solo Online+Zeit |
| `/w/alert` | Solo Alert-Widget |
| `/w/music` | Solo Music Player (Spotify / YouTube Music) |
| `/w/spotify` | Alias für `/w/music` |
| `/w/chat` | Solo Chat-Widget |
| `/w/ending-stats` | Solo Ending-Stats (Stream-Statistik) |
| `/w/text` | Solo Text-Widget |
| `/w/image` | Solo Image-Widget |
| `/w/countdown` | Solo Countdown (globaler App-State) |
| `/w/socials` | Solo Socials (eine Plattform, Auswahl per Prop) |
| `/w/partner-roulette` | Solo Partner Roulette (Partner-Logos rotieren) |
| `/w/shape/{shapeId}` | Solo Frame/Shape, z. B. `/w/shape/frame` |
| `GET/PUT /layout/{id}` | Layout laden/speichern (PUT nur Loopback) |
| `/canvas/…` | Embedded Assets (CSS/JS) |
| `GET /obs/video-settings` | OBS-Base-/Output-Auflösung (`connected`, `baseWidth`, …) |
| `GET /obs/preview` | PNG-Screenshot der aktuellen Programmszene (Editor-Vorschau) |

Beispiel: `/editor/default`, `/view/just-chatting`.

## Editor-Hilfen (nur `/editor`, nicht `/view`)

- **32px Padding** um die Zeichenfläche (Fit/Scale berücksichtigt den Abstand).
- **OBS-Vorschau** (Toolbar-Toggle, default aus): periodischer Screenshot nur auf `.ccs-canvas`; Prefs in `localStorage` (`ccs-editor-prefs`).
- **Canvas-Größe von OBS**: Anzeige der Base-Auflösung + Button **Von OBS übernehmen** (`baseWidth`/`baseHeight`).
- **Raster** ein/aus, Unterteilungen **H × V** (Default 16×6), nur visuell.
- **Magnet** ein/aus: Snap an Kanten/Mitten **anderer Widgets** (Threshold 8px) inkl. Guide-Linien.
- **Rechtsklick-Menü** / Toolbar: Duplizieren, Sperren/Entsperren, Ganz nach oben/unten, Ebene rauf/runter, Löschen.

## Props-Panel

Eigenschaften sind gruppiert (`propSection`, ein-/ausklappbar; **Position & Größe** default eingeklappt). Features nutzen LiveFX-artige `featureSection`-Toggles. Farben: `colorProp` (Picker + Swatches), Schriften: `fontProp` (Typeahead + Dropdown).

**Effekte:** Jedes Item hat `effects[]` (Glow, Particles, Scanlines, Vignette, Blur, Noise). Im Props-Panel unten stapelbar, einzeln aktivierbar. Weitere Modifier: Skill `overlay-effect` / Packs: `overlay-extension-pack`.

## TypeScript-Quellen

Editor/Runtime liegen unter `src/CreatorControlSuite.Modules.Overlay/CanvasOverlay/src/` und werden per `npm run build` (MSBuild-Target) nach `shared/runtime.js` / `editor/editor.js` gebündelt. Die Bundles sind gitignored und entstehen lokal bzw. in CI beim Build.

## OBS-Setup

**Variante A – mehrere Canvases:** Pro Design eine Browserquelle auf `/view/{id}` (Canvas-Auflösung, transparent). In OBS-Szenen die passende Quelle ein-/ausblenden.

**Variante B – Einzelquellen:** jeweils `/w/…` oder `/w/shape/…` als eigene Browserquelle.

Standalone-Chat bleibt zusätzlich unter `/chat` verfügbar.

## Widgets

| Typ | Daten |
|-----|--------|
| `online` | `stream.isLive`, Uhr, Uptime |
| `alert` | WS `app.alert` / Twitch-Events |
| `music` | Now Playing aus aktivem Music-Provider; Alias-Typ `spotify`. Props: `variant` (22 Styles), `sizePreset` (Mini–XL/Banner/Cover), Anzeige-Toggles; Titel/Artist/Album scrollen bei Overflow (Marquee); skaliert responsiv |
| `chat` | WS `channel.chat.message` (+ optional Twitch-Events); Appearance/Font per Widget-Props (Fallback: Overlay-Chat-Einstellungen); Session-History via `/chat/history` + WS-Replay |
| `ending-stats` | Session-Stats (`stats.*`) + Followerziel (`twitch.followers` / `followerGoal`); Prop `variant` mit 10 Looks; skaliert bei Größenänderung |
| `socials` | Ein Social-Link pro Widget (`platform` + `handle`/`url`/`label`/`iconUrl`); für YT+Twitch zwei Instanzen; Icons SVG oder Font Awesome; Props `variant` / `iconLibrary` |
| `text` | Statischer Text aus Props (`content`, Typografie, Ausrichtung, Schatten) |
| `image` | Bild aus URL (`src`, `fit`, Opacity, Radius) |
| `countdown` | Globaler Countdown aus `countdown.*` (Dashboard / Workflow / Automationen); Props: `variant`, `format`, `showLabel`, `hideWhenIdle` |
| `partner-roulette` | Partner-Logos/-Bilder rotieren; Props: `images[]`, `intervalMs`, `transition` (`fade`/`crossfade`/`slide`/`none`), `transitionMs`, `fit`, `borderRadiusPx` |

## Globaler Countdown

Der Typ `countdown` zeigt den **gemeinsamen** App-Countdown (`overlay-data.json` → `countdown`), nicht einen lokalen Timer pro Widget.

Steuerung:

- Dashboard-Modul **COUNTDOWN** in der Titlebar neben SESSION (Start / Stop / Reset / Zahnrad)
- Zahnrad: Label, Dauer, Presets 5 / 10 / 30 Minuten
- Workflow-Buttons „Countdown“ / „Countdown stoppen“
- Timed Automation Action `OverlayCountdown` (`Start` / `Stop`, optional eigene Dauer)
- IPC `workflow.countdown` / `workflow.countdown.stop`
- Automatischer Streamstart-Countdown (`StartWorkflowCountdownAfterObsStreamStart`)

## Shapes / Frames

`frame`, `frame.card`, `shape.vignette`, `shape.cutout`, `shape.scene-bg`.

Legacy-Typen `frame.rect` / `frame.circle` / `frame.corners` / `frame.bevel` / `frame.neon` / `frame.dashed` bleiben renderbar (Map auf `mode`), sind aber nicht mehr in der Palette.

### Frame (`frame`)

Einheitlicher Rahmen mit Modus-Auswahl. Props: `mode`, `color`, `radius` (Eckenradius in px, Default `16`).

**26 Modi (`mode`):**

- Klassisch: `rect`, `circle`, `corners`, `bevel`, `neon`, `dashed`
- Kreativ: `double`, `dotted`, `groove`, `ridge`, `pixel`, `ticket`, `stamp`, `film`, `hud`, `hex`, `octagon`, `tape`, `scan`, `rainbow`, `comic`, `frosted`, `chrome`, `notch`, `brackets`, `orbit`

Solo-URL: `/w/shape/frame` (optional `?props={"mode":"neon","radius":24,"color":"#00e5ff"}`).

### Cutout (`shape.cutout`)

Schneidet ein Loch in alles darunter auf dem Canvas (SVG-Luminanz-Maske → echte Alpha-Transparenz). In OBS (Browserquelle mit Transparenz) scheint die darunterliegende Szene durch. Items mit höherem `z` bleiben unberührt.

**Props:** `radius` — Eckenradius in px (Default `24`).

Solo-URL: `/w/shape/shape.cutout` (optional `?props={"radius":48}`).

### Card Frame (`frame.card`)

Port der Desktop-`card-frame-only`-Rahmen (Just Chatting, Square, Metaschutz, Start, BRB, Ending): Sweep, Topline/Bottomline und Corner-Borders, fluid in der Item-Box.

**8 Varianten (`variant`):** `classic`, `neon`, `soft`, `bold`, `outline`, `glass`, `cyber`, `minimal`.

**Größen-Presets (`sizePreset`):** setzen `w`/`h` — `chatting` 1060×420, `square` 500×500, `metaschutz`/`start` 1060×500, `brb` 1060×420, `ending` 920×500. Danach frei skalierbar.

**Farben / Props:** `color` (Akzent), `color2` (Sweep/Glow), `fillOpacity` (0–1), `showSweep`, `showLines`.

Solo-URL: `/w/shape/frame.card` (optional `?props={"variant":"neon","color":"#00e5ff"}`).

### Starting Hintergrund (`shape.scene-bg`)

Animierter Szenen-Hintergrund (Glow, Streifen, Partikel). Farben und Animation sind über Props steuerbar.

**10 Variationen (`preset`):** `ember`, `crimson`, `aurora`, `violet`, `gold`, `ice`, `lime`, `magenta`, `steel`, `inferno`.

Wichtige Props: `glow1`/`glow2`, `bgBase`/`bgMid`/`bgDeep`, `speed` (1 = normal), `driftDuration`/`particleDuration`, `stripes`/`particles`/`paused`, Opacity-Werte für Glow/Streifen/Partikel/Vignette/Scan.

Solo-URL: `/w/shape/shape.scene-bg` (optional `?props={"preset":"aurora","speed":1.5}`).

## Persistenz

`%LocalAppData%\CreatorControlSuite\Overlay\layouts\{id}.json`

Canvas-Namen und Auswahl: App-Einstellungen (`Overlay.Canvases`, `Overlay.SelectedCanvasId`).
