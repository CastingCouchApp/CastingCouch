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
| `/w/goal-bar` | Solo Goal Bar (Follower/Sub/Bit/Custom) |
| `/w/event-ticker` | Solo Event Ticker |
| `/w/viewer-count` | Solo Viewer Count |
| `/w/lower-third` | Solo Lower Third |
| `/w/qr-code` | Solo QR Code |
| `/w/brb-panel` | Solo BRB / Starting Panel |
| `/w/announcement-bar` | Solo Announcement Bar |
| `/w/animated-background` | Solo Animated Background (34 Styles, inkl. Parallax-Berge) |
| `/w/shape/{shapeId}` | Solo Frame/Shape, z. B. `/w/shape/frame` |
| `GET/PUT /layout/{id}` | Layout laden/speichern (PUT nur Loopback) |
| `/canvas/…` | Embedded Assets (CSS/JS) |
| `GET /obs/video-settings` | OBS-Base-/Output-Auflösung (`connected`, `baseWidth`, …) |
| `GET /obs/preview` | PNG-Screenshot der aktuellen Programmszene (Editor-Vorschau) |

Beispiel: `/editor/default`, `/view/just-chatting`.

## Editor-Hilfen (nur `/editor`, nicht `/view`)

- **Fenstergröße**: Modal über Toolbar-Button (Badge mit aktueller Größe); Preset / B×H, **Größe anwenden**, OBS-Base-Auflösung + **Von OBS übernehmen**.
- **OBS-Vorschau** / **Raster** / **Einrasten** / **Magnet** (Toolbar-Icon-Toggles; Tooltips per `title`; Prefs in `localStorage` `ccs-editor-prefs`).
- **Widget-Palette**: Kategorien (Live, Interaktion, Content, Hintergrund, Frames, Masken, Deko) + Suche (Label/Typ/Kategorie/Keywords). Installierte Extension-Packs erscheinen zusätzlich unter `Extension · {Pack-Name}` (`ext:{packId}:{id}`). Hover auf einer Karte zeigt eine Live-Vorschau mit Demodaten (skaliert).
- **32px Padding** um die Zeichenfläche (Fit/Scale berücksichtigt den Abstand).
- **Raster** Unterteilungen **H × V** (Default 32×18 für 16:9; `gridDivisionsForCanvas` leitet V aus dem Canvas-Seitenverhältnis ab); liegt immer im Vordergrund über den Widgets (`pointer-events: none`). Editor-Layer (Grid/OBS-Vorschau) werden nach jedem `setLayout`/`renderItems` via `onAfterRender` neu gesetzt.
- **Einrasten**: Snap an Rasterlinien (H×V) beim Verschieben **und** Skalieren (nur die gezogene Handle-Kante); Threshold ~20 % der Zellengröße (min. 8px), inkl. Guide-Linien. Unabhängig von der Raster-Anzeige.
- **Magnet**: Snap an Kanten/Mitten **anderer Widgets** (Threshold 8px) beim Verschieben/Skalieren, inkl. Guide-Linien. Bei gleichzeitigem Einrasten gewinnt der Magnet, wenn er greift.
- **Rechtsklick-Menü** / Toolbar-Icons / **Entf** bzw. **Backspace**: Duplizieren, Sperren/Entsperren, Ganz nach oben/unten, Ebene rauf/runter, Löschen (nicht bei Fokus in Eingabefeldern; gesperrte Items bleiben).

## Props-Panel

Affinity-orientierter Inspector (Cool-Gray): Tabs **Layout** / **Widget** / **Effekte** / **Animationen**. Im Widget-Tab: **Inhalt** und **Look** default offen, **Stil** und **Erweitert** default zugeklappt (`contentSection` / `lookSection` / `styleSection` / `advancedSection`). Layout-Tab: **Position & Größe** default zugeklappt — inkl. einheitliches **Padding** (px) auf Item-Ebene für alle Widgets/Shapes. Effekte-/Animationen-Tabs ohne zusätzlichen Section-Wrapper. Alle Props teilen das kompakte Row-Layout **Label | Control** (Zahlen: Label | Slider | Wert). Features nutzen LiveFX-artige `featureSection`-Toggles (in **Erweitert**). Farben: `colorProp` (Expand-Handle + Picker; Swatches rechtsbündig mit Historie + Presets), Schriften: `fontProp`. Aktiver Tab in `sessionStorage` (`ccs-props-tab`).

**Effekte:** Jedes Item hat `effects[]` — stapelbare Modifier (Glow, Particles, Scanlines, Vignette, Blur, Noise, Neon, Glitch, Sparkle, Aurora, Pulse Ring, Hologram, Outline, Drop Shadow, Rainbow, Spotlight) plus Pack-Effekte aus installierten Extension Packs (`ext:…`). Pro Effekt optional **Ziel** `box`/`content` — nur wenn die Strategy `targets` inkl. `content` hat (Glow, Drop Shadow, Outline, Glitch); sonst kein Ziel-Select. Glow / Outline / Drop Shadow: **Animation** `Aus` / `Pulse` / `Atmen` + Tempo. Im Tab **Effekte**, einzeln aktivierbar. Weitere Modifier: Skill `overlay-effect` / Packs: `overlay-extension-pack`.

**Animationen:** Jedes Item hat `animations[]` — Motion auf dem Content (Fade, Slide, Bounce, Pop, Shake, Float, Pulse, Spin, Wiggle, Flip) plus Pack-Animationen (`ext:…`). Im Tab **Animationen**; `loop` und Dauer steuerbar.

## TypeScript-Quellen

Editor/Runtime liegen unter `src/CreatorControlSuite.Modules.Overlay/CanvasOverlay/src/` und werden per `npm run build` (MSBuild-Target) nach `shared/runtime.js` / `editor/editor.js` gebündelt. Die Bundles sind gitignored und entstehen lokal bzw. in CI beim Build.

## Browser-Dev (ohne WPF-App)

Cross-platform (Node 18+): simulierter Overlay-Webserver inkl. Editor, View, Solo-Widgets, Standalone-Chat, Layout-API, `overlay-data`, WebSocket-Events und Hot-Reload.

```bash
# aus Repo-Root
make canvas-dev

# oder
cd src/CreatorControlSuite.Modules.Overlay/CanvasOverlay
npm install
npm run dev
```

Öffnen: `http://127.0.0.1:8765/` (gleiche Routen wie der App-Overlay-Server). Port: `CCS_DEV_PORT`, Sim aus: `CCS_DEV_SIM=0`.

Persistenz: `CanvasOverlay/dev/.layouts/`, `dev/.data/` (gitignored). Extension-Packs optional unter `dev/extensions/{packId}/`.

## OBS-Setup

**Variante A – mehrere Canvases:** Pro Design eine Browserquelle auf `/view/{id}` (Canvas-Auflösung, transparent). In OBS-Szenen die passende Quelle ein-/ausblenden. WebSocket reconnectet automatisch und lädt Chat-Config/-Historie bei jedem Connect neu.

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
| `goal-bar` | Live-Zielleiste (`followers`/`subs`/`bits`/`custom`); `variant` (≥12), `sizePreset`, Show-Toggles, `fillStyle`, `colorProp`/`fontProp`, Features `hideWhenComplete` / `pulseOnProgress` |
| `event-ticker` | Event-Laufschrift aus Twitch-/Alert-Events; `variant`, Marquee/Fade/Liste, Template, Sources |
| `viewer-count` | Live-Zuschauerzahl + Peak; `stream.viewerCount` / `stats.peakViewers` |
| `lower-third` | Namenszeile (Name/Subtitle/Tag/Avatar); viele Broadcast-Looks |
| `qr-code` | QR aus URL (clientseitig) + Caption/Logo; `errorCorrection`, `fg`/`bg` |
| `brb-panel` | BRB/Starting/Tech-Pause Panel; optional globaler `countdown.*` |
| `announcement-bar` | Ankündigungsbanner mit optionalem Marquee |
| `animated-background` | Animierter Full-Bleed-Hintergrund mit **34 Styles**: CSS-Looks (`cyber` … `noir`), **JS-Matrix-Rain** (`hacker`: fallende Katakana/Symbole), plus **JS-Parallax-Berge** (`mountains` … `ridge-storm`); Props: `variant`, `sizePreset`, `color`/`color2`/`color3`, `speed`, `intensity`, `density`, `opacity`, Features `vignette` / `paused` |

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

`frame`, `frame.card`, `shape.vignette`, `shape.cutout`, `shape.scene-bg`, `shape.divider`, `shape.cam-ring`, `shape.sticker`.

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

### Divider (`shape.divider`)

Zierlinie mit ≥12 Styles (`line`/`dashed`/`glow`/`flourish`/…). Props: `orientation`, `thickness`, Motif, `color`/`color2`, Feature `animateShimmer`.

### Cam Ring (`shape.cam-ring`)

Webcam-Ring + Badge (`live`/`rec`/`custom`); Varianten `ring`/`hex`/`neon`/…; Features `pulse`/`rotateSlow`. Oft kombiniert mit `shape.cutout`.

### Sticker (`shape.sticker`)

Dekoratives Sticker-Item: Presets (heart/star/…) oder Custom-`src`, Hüllen-Varianten, Features `bob`/`spin`/`pulse`.

## Persistenz

`%LocalAppData%\CreatorControlSuite\Overlay\layouts\{id}.json`

Canvas-Namen und Auswahl: App-Einstellungen (`Overlay.Canvases`, `Overlay.SelectedCanvasId`).
