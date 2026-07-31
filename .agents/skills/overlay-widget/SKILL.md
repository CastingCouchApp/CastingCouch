---
name: overlay-widget
description: >-
  Führt ein neues Canvas-Overlay-Widget (oder Shape) in die Creator Control
  Suite ein: TDD, TypeScript-Module unter CanvasOverlay/src, co-located CSS,
  Editor-Props (Tabs Layout/Widget/Effekte/Animationen; contentSection/lookSection/
  styleSection/advancedSection; prop-row Controls), ListWidgetTypes, App-URL-Combo,
  Docs und Tests. Nutzen bei neuem Overlay-Widget, Canvas-Widget, Solo-Widget
  `/w/…`, Frame/Shape. Für Effect-Modifier → overlay-effect; für ZIP-Packs →
  overlay-extension-pack.
---

# Overlay-Widget einführen

Canvas-Overlays sind **kein** separates HTML pro Karte. Built-in Widgets leben
als TypeScript-Module unter `CanvasOverlay/src/shared/widgets/<name>/`, werden
per esbuild nach `shared/runtime.js` gebündelt und embedded ausgeliefert.

TDD: zuerst Tests, dann Implementation. Keine Widget-Änderung ohne passende Assertions.

## Fortschritt

```
Overlay Widget Progress:
- [x] 1. Typ/Daten/Props klären (Inhalt/Look/Stil/Erweitert; color/font; bool-Keys)
- [x] 2. Tests (rot) — Bundle-Symbole + List*Types
- [x] 3. shared/widgets/<name>/ oder shapes/<name>/ + co-located CSS
- [x] 4. ListWidgetTypes / ListShapeTypes
- [x] 5. editor/props (sync-props oder panels/<name>/) + Palette — Sections + Controls
- [x] 6. npm run build (CanvasOverlay)
- [x] 7. App Solo-URL-Combo
- [x] 8. Docs
- [x] 9. Tests grün
```

<!-- Last widget: chat (configurable + twitch colors + persistence + clear/delete) -->

## Architektur

| Rolle | Pfad |
|-------|------|
| Widget-Modul | `CanvasOverlay/src/shared/widgets/<name>/` |
| Shape-Modul | `CanvasOverlay/src/shared/shapes/<name>/` |
| Frame (unified) | `shapes/frame/` — Typ `frame`, Prop `mode` (+ Legacy `frame.*`) |
| Editor-Panel | `CanvasOverlay/src/editor/props/` (+ ggf. `panels/<name>/`) |
| Controls | `…/editor/controls/` (`numProp`, `textProp`, `selectProp`, `boolProp`, `fontProp`, `colorProp`) |
| Sections | `…/editor/sections/prop-section.ts` (`contentSection` / `lookSection` / `styleSection` / `advancedSection`, `featureSection`) |
| Tabs | Layout / Widget / Effekte / Animationen (`inspector-tabs.ts`, `sessionStorage` `ccs-props-tab`) |
| Bundle | `shared/runtime.js`, `editor/editor.js` (generiert, **nicht** versioniert) |
| Look | `shared/styles.css` / `editor/editor.css` (gebündelt, **nicht** versioniert) |
| Shell-CSS | `…/editor/editor-shell.css` (Inspector-Rows, `.ccs-check`, Sections) |
| Whitelist | `CanvasOverlayAssets.ListWidgetTypes` / `ListShapeTypes` |
| Build | `CanvasOverlay/` → `npm run build` (MSBuild-Target) |

URLs: Canvas `/view/{id}`, Editor `/editor/{id}`, Solo Widget `/w/{type}`, Solo Shape `/w/shape/{type}`.

Naming: Typ **lowercase** / `kebab-case`; CSS-Prefix **`ccs-`**; keine Standalone-HTML-Module.

## Inspector-Design (Pflicht)

Affinity-artiger Cool-Gray-Inspector. Alle Widget-Props landen im Tab **Widget**.

### Tabs

| Tab | Inhalt |
|-----|--------|
| **Layout** | Typ, Position & Größe (`propSection("geometry", …, true)` collapsed; inkl. **Padding** auf Item-Ebene), Gesperrt |
| **Widget** | Widget-/Shape-Props (Sections unten) |
| **Effekte** | `renderEffectsPanel` — **flach**, kein äußerer `propSection("Effekte")` |
| **Animationen** | `renderAnimationsPanel` — **flach**, kein äußerer `propSection("Animationen")` |

`[hidden]` an Form/Panes braucht `display: none !important` (Author-`display:flex` überschreibt sonst `hidden`).

### Sections im Widget-Tab

Reihenfolge und Defaults:

| Helper | Titel | Default |
|--------|-------|---------|
| `contentSection(id)` | Inhalt | **offen** |
| `lookSection(id)` | Look (Variant/Size) | **offen** |
| `styleSection(id)` | Stil (Farben/Fonts/Abstände) | **zugeklappt** |
| `advancedSection(id)` | Erweitert (Toggles/Extras) | **zugeklappt** |

Layout-Geometrie bleibt im Layout-Tab collapsed. Session-State: `ccs-prop-section:<id>`.

### Controls — einheitliches Row-Layout

Alle Controls nutzen `.ccs-prop-row` (**Label 88px | Control**), analog zu Zahlen:

- `numProp` → Label \| Slider \| Wert (`.ccs-num-prop`, 3 Spalten)
- `textProp` / `selectProp` / `fontProp` → Label \| Input/Select
- `colorProp` → Label \| Expand-Handle \| Picker+Text; Swatches rechtsbündig erst nach Expand (Handle links neben Picker). Oben **Historie** (`localStorage` `ccs-color-history`, max. 12, MRU), darunter Presets.
- `boolProp` → Label \| Checkbox (`.ccs-check`)

**Keine** vertikal gestapelten `<label>Text<input>` mehr für neue Props. Keine Freitext-Farbe/Font.

### Checkboxen

- Klasse **`.ccs-check`** (14×14, Cool-Gray-Rahmen, Akzent `#4a90d9` wie Tabs).
- Einfache On/Off-Flags → **`boolProp`** (auch in **Erweitert**), nicht `featureSection`.
- `featureSection` nur wenn Unterfelder existieren (z. B. Sticker Bob mit Amplitude); Header ebenfalls Label \| Checkbox (`.ccs-prop-row`).
- Settings bleiben erhalten wenn aus (`enabledKey`).

### Anti-Patterns (UI)

- `featureSection` für einfache Bools (sieht anders aus als `boolProp` / „Inner Glow“)
- Äußerer „Effekte“-/„Animationen“-`propSection` im jeweiligen Tab
- Abweichende Checkbox-Styles ohne `.ccs-check`
- Props ohne `.ccs-prop-row` (außer Effect-/Animation-Karten-Header)

## Settings-Conventions (kurz)

- Gruppen: `contentSection` / `lookSection` (offen), `styleSection` / `advancedSection` (zugeklappt).
- Farben → `colorProp`, Schriften → `fontProp`.
- Item-Effekte **nicht** als Widget-Props — Effects-Panel / `overlay-effect` / Packs.
- Palette: kategorisiert (`Live`, `Interaktion`, `Content`, `Community Widgets`, `Hintergrund`, `Frames`, `Masken`, `Deko`) + Suche (`#paletteSearch`, filtert Label/Typ/Kategorie/Keywords). Hover auf Karte → Live-Vorschau mit Demodaten (`palette-preview.ts` + `palette-demo.ts`, `paintItemContent`). Community-Widgets (z. B. `fruppis-landadel`) unter **Community Widgets**.

### Goal-Bar-Synchronisation

- Twitch-Goals sind die zentrale Voreinstellung für `goal-bar`: neue Items ohne explizites `label`/`target` lesen `twitch.*GoalState` aus den Overlay-Daten.
- Beim Speichern unter **Dienste → Twitch → Streamziele** müssen vorhandene `goal-bar`-Items in **allen** Canvas-Layouts über `OverlayGoalLayoutUpdater` aktualisiert werden.
- Zu synchronisieren: `label`, `target`; ein gespeichertes `current`-Override wird entfernt, damit aktuelle Follower-/Sub-/Bits-Werte wieder aus Twitch kommen.
- Nach jedem geänderten Layout: `IOverlayLayoutStore.SaveAsync` und `OverlayEventBridge.AppOverlayLayout` publizieren, damit bereits laufende Overlay-Views und der Editor sofort aktualisiert werden.

## Build

```bash
cd src/CreatorControlSuite.Modules.Overlay/CanvasOverlay
npm install
npm run build
# Browser-Dev mit Hot-Reload + Overlay-Server-Simulation (ohne WPF):
npm run dev   # → http://127.0.0.1:8765/editor/default
# oder aus Repo-Root: make canvas-dev
```

dotnet build führt das MSBuild-Target `BuildCanvasOverlay` automatisch aus.
Die Bundles (`runtime.js`, `editor.js`, …) sind gitignored — nur `src/` und Build-Config committen.

## Verifikation

```bash
cd src/CreatorControlSuite.Modules.Overlay/CanvasOverlay && npm test
dotnet test tests/CreatorControlSuite.Tests/CreatorControlSuite.Tests.csproj --filter "FullyQualifiedName~CanvasOverlayAssetsTests"
```

Relevante Vitest-Suites: `prop-section-defaults`, `prop-row-layout`, `checkbox-style`, `inspector-tabs`.

## Anti-Patterns

- Gottfile erweitern statt `widgets/<name>/`
- Farbe/Font als `textProp`
- Effekt-Logik in Widget-`update` hardcoden
- Alias wie `spotify` in `ListWidgetTypes` listen

## Spezial: Cutout (`shape.cutout`)

Transparenz-Loch über SVG-Luminanz-Maske (`applyCutoutStackMask`): wrappt bereits gemalte
Items in `.ccs-cutout-stack` und stanzt `radius`-abgerundete Löcher. Modul: `shapes/cutout/`.
Im Editor Hatch auf dem Placeholder-Item; Live-View bleibt klar (kein schwarzes Fill).
Nicht `destination-out` — bricht in OBS/CEF-Compositing oft zu opakem Schwarz.

## Weiterführend

- Effects: Skill `overlay-effect`
- ZIP-Packs: Skill `overlay-extension-pack`
- Docs: `docs/modules/OVERLAY-EDITOR.md`, `OVERLAY-SYSTEM.md`
