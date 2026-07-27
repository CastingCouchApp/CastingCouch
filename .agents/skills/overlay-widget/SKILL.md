---
name: overlay-widget
description: >-
  Führt ein neues Canvas-Overlay-Widget (oder Shape) in die Creator Control
  Suite ein: TDD, TypeScript-Module unter CanvasOverlay/src, co-located CSS,
  Editor-Props (propSection/featureSection/fontProp/colorProp), ListWidgetTypes,
  App-URL-Combo, Docs und Tests. Nutzen bei neuem Overlay-Widget, Canvas-Widget,
  Solo-Widget `/w/…`, Frame/Shape. Für Effect-Modifier → overlay-effect; für
  ZIP-Packs → overlay-extension-pack.
---

# Overlay-Widget einführen

Canvas-Overlays sind **kein** separates HTML pro Karte. Built-in Widgets leben
als TypeScript-Module unter `CanvasOverlay/src/shared/widgets/<name>/`, werden
per esbuild nach `shared/runtime.js` gebündelt und embedded ausgeliefert.

TDD: zuerst Tests, dann Implementation. Keine Widget-Änderung ohne passende Assertions.

## Fortschritt

```
Overlay Widget Progress:
- [ ] 1. Typ/Daten/Props klären (featureSection-Keys, color/font)
- [ ] 2. Tests (rot) — Bundle-Symbole + List*Types
- [ ] 3. shared/widgets/<name>/ oder shapes/<name>/ + co-located CSS
- [ ] 4. ListWidgetTypes / ListShapeTypes
- [ ] 5. editor/props/panels/<name>/ + Palette (propSection / featureSection / fontProp / colorProp)
- [ ] 6. npm run build (CanvasOverlay)
- [ ] 7. App Solo-URL-Combo
- [ ] 8. Docs
- [ ] 9. Tests grün
```

## Architektur

| Rolle | Pfad |
|-------|------|
| Widget-Modul | `CanvasOverlay/src/shared/widgets/<name>/` |
| Shape-Modul | `CanvasOverlay/src/shared/shapes/<name>/` |
| Frame (unified) | `shapes/frame/` — Typ `frame`, Prop `mode` (+ Legacy `frame.*`) |
| Editor-Panel | `CanvasOverlay/src/editor/props/` (+ ggf. `panels/<name>/`) |
| Controls | `…/editor/controls/` (`fontProp`, `colorProp`, …) |
| Sections | `…/editor/sections/` (`propSection`, `featureSection`) |
| Bundle | `shared/runtime.js`, `editor/editor.js` (generiert, **nicht** versioniert) |
| Look | `shared/styles.css` (gebündelt, **nicht** versioniert) |
| Whitelist | `CanvasOverlayAssets.ListWidgetTypes` / `ListShapeTypes` |
| Build | `CanvasOverlay/` → `npm run build` (MSBuild-Target) |

URLs: Canvas `/view/{id}`, Editor `/editor/{id}`, Solo Widget `/w/{type}`, Solo Shape `/w/shape/{type}`.

Naming: Typ **lowercase** / `kebab-case`; CSS-Prefix **`ccs-`**; keine Standalone-HTML-Module.

## Settings-Conventions

- Gruppen: `propSection(id, title, collapsed?)` — Position/Größe kommt aus common/geometry (default collapsed).
- LiveFX: `featureSection({ enabledKey })` — Settings bleiben erhalten wenn aus.
- Farben → `colorProp`, Schriften → `fontProp` (kein Freitext für Farbe/Font).
- Item-Effekte **nicht** als Widget-Props — Effects-Panel / `overlay-effect` / Packs.

## Build

```bash
cd src/CreatorControlSuite.Modules.Overlay/CanvasOverlay
npm install
npm run build
```

dotnet build führt das MSBuild-Target `BuildCanvasOverlay` automatisch aus.
Die Bundles (`runtime.js`, `editor.js`, …) sind gitignored — nur `src/` und Build-Config committen.

## Verifikation

```bash
dotnet test tests/CreatorControlSuite.Tests/CreatorControlSuite.Tests.csproj --filter "FullyQualifiedName~CanvasOverlayAssetsTests"
```

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
