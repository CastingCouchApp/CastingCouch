---
name: overlay-widget
description: >-
  Führt ein neues Canvas-Overlay-Widget (oder Shape) in die Creator Control
  Suite ein: TDD, runtime.js/styles.css, Editor-Palette/Props,
  ListWidgetTypes, App-URL-Combo, Docs und Tests. Nutzen bei neuem Overlay-
  Widget, Canvas-Widget, Solo-Widget `/w/…`, Ending-Stats-artigem Panel,
  Frame/Shape oder „Widget in Overlay einbauen“.
---

# Overlay-Widget einführen

Canvas-Overlays sind **kein** separates HTML pro Karte. Widgets leben in
`CanvasOverlay/shared/runtime.js` + `styles.css`, werden embedded ausgeliefert
und im Editor per Typ registriert.

TDD: zuerst Tests, dann Implementation. Keine Widget-Änderung ohne passende Assertions.

## Fortschritt

```
Overlay Widget Progress:
- [ ] 1. Typ/Daten/Props klären
- [ ] 2. Tests schreiben (rot)
- [ ] 3. runtime.js (Defaults, create, update, Dispatch)
- [ ] 4. styles.css (.ccs-…)
- [ ] 5. ListWidgetTypes / ListShapeTypes
- [ ] 6. editor.js (Palette + Props)
- [ ] 7. App Solo-URL-Combo
- [ ] 8. Docs
- [ ] 9. Tests grün
```

## Architektur (kurz)

| Rolle | Pfad |
|-------|------|
| Defaults / DOM / Datenbindung | `src/CreatorControlSuite.Modules.Overlay/CanvasOverlay/shared/runtime.js` |
| Look | `…/CanvasOverlay/shared/styles.css` |
| Typ-Whitelist | `…/CanvasOverlayAssets.cs` → `ListWidgetTypes()` / `ListShapeTypes()` |
| Editor-UI | `…/CanvasOverlay/editor/editor.js` |
| Solo-URL-Combo (WPF) | `src/CreatorControlSuite.App/Shell/MainWindow.xaml.cs` → `EnsureOverlayWidgetUrlCombo` |
| Live-Daten | `…/Models/OverlayModels.cs` + Writer in App (`overlay-data.json`) |
| Docs | `docs/modules/OVERLAY-EDITOR.md`, `OVERLAY-SYSTEM.md` |
| Tests | `tests/CreatorControlSuite.Tests/CanvasOverlayAssetsTests.cs` |

URLs: Canvas `/view/{id}`, Editor `/editor/{id}`, Solo Widget `/w/{type}`, Solo Shape `/w/shape/{type}`.

**Widgets** (`kind: widget`): `online`, `alert`, `music` (+ Alias `spotify`), `chat`, `ending-stats`, `text`, `image`, `countdown`, …  
**Shapes** (`kind: shape`): `frame.*`, `shape.vignette`, `shape.scene-bg`, …

Naming: Typ **lowercase** / ggf. `kebab-case`; CSS/JS-Prefix **`ccs-`**; keine neuen Standalone-HTML-Module.

## 1. Typ / Daten / Props klären

- Widget-Typ-ID festlegen (z. B. `ending-stats`).
- Welche Felder aus `overlay-data.json` / WS-Events? Bei Lücken: `OverlayModels` + App-Writer erweitern.
- Props: Booleans/Numbers/Text/Select (`variant`, Appearance, …). Defaults in `WIDGET_DEFAULTS`.
- Default-Größe `w`/`h` (Canvas-Pixel).
- Bei Design-Varianten: **ein** Typ + Prop `variant` (Select), nicht 10 separate Typen — außer wirklich unterschiedliche Shapes.

## 2. Tests zuerst (rot)

In `CanvasOverlayAssetsTests.cs`:

1. `ListWidgetTypes()` enthält den neuen Typ (Aliases wie `spotify` **nicht** in der Liste).
2. Runtime enthält Defaults-Key, `create…El`, `update…`, relevante Datenfelder / Observer.
3. Editor enthält `type: "…"`, Props (`selectProp("variant"` etc.).
4. CSS enthält Root-Klasse und Varianten-Klassen.

Vorbild: bestehende Facts für `chat` / `ending-stats` / `shape.scene-bg`.

## 3. `runtime.js`

1. Eintrag in `WIDGET_DEFAULTS` (oder `SHAPE_DEFAULTS`).
2. `createXxxEl(item)` → DOM mit Klassen `ccs-…`.
3. `updateXxx(el, item, data)` (und ggf. `paint…` für Tick ohne Re-Layout).
4. Branch in:
   - `createItemContent`
   - `refreshItemData`
   - bei Bedarf `tick`, `handleRealtime`
5. Responsive (wenn gefordert): `ResizeObserver` + CSS-Variablen / `container-type`; Inhalt `width/height: 100%` der Item-Box.
6. Alias nur bei Legacy-URLs (wie `spotify` → music-Logic), **ohne** Eintrag in `ListWidgetTypes`.

## 4. `styles.css`

- Root: `.ccs-{name}` mit `width/height: 100%`, `box-sizing: border-box`.
- Varianten: `.ccs-{name}-v-{id}`.
- Fluid: `clamp` / `--ccs-*-scale` / `@container` — kein festes 1920×1080-Layout im Widget.

## 5. C#-Whitelist

`CanvasOverlayAssets.ListWidgetTypes()` bzw. `ListShapeTypes()` um den Typ erweitern. Solo-Serving und Health nutzen diese Listen.

## 6. `editor.js`

1. Palette-Array `widgets` oder `shapes`: `{ type, label }`.
2. In `syncProps`: Props via `boolProp` / `numProp` / `textProp` / `selectProp`.
3. Props schreiben `item.props[key]` → `runtime.renderItems()` + `scheduleSave()`.

## 7. App-Combo

`EnsureOverlayWidgetUrlCombo`: Eintrag `("Widget: …", "{type}")` bzw. `("Shape: …", "shape/{type}")`.

## 8. Docs

`docs/modules/OVERLAY-EDITOR.md`: Solo-URL-Zeile + Widgets-/Shapes-Tabelle. Bei System-Verhalten ggf. `OVERLAY-SYSTEM.md`.

## 9. Verifikation

```bash
dotnet test tests/CreatorControlSuite.Tests/CreatorControlSuite.Tests.csproj --filter "FullyQualifiedName~CanvasOverlayAssetsTests"
```

Manuell (optional): Overlay-Webserver → Editor → Widget platzieren → `/view/{id}` und `/w/{type}` prüfen.

## Anti-Patterns

- Neue Datei unter `modules/cards/*.html` oder externe `/o/{id}/`-Ordner — entfallen.
- Widget nur in CSS/JS, aber nicht in `ListWidgetTypes` / Editor / Tests.
- Festpixel-Typografie ohne Resize-Reaktion, wenn der User Responsive verlangt.
- `spotify`-Pattern kopieren und den Alias fälschlich in `ListWidgetTypes` listen.

## Beispiel: `ending-stats`

- Defaults: `w: 980`, `h: 220`, `props.variant`, `props.showTitle`
- Daten: `stats.*`, `twitch.followers` / `followerGoal`
- 10 Looks über `variant` + CSS-Klassen `ccs-ending-stats-v-*`
- Fit via `ResizeObserver` + `container-type`

## Weiterführend

- Systemüberblick: `docs/modules/OVERLAY-SYSTEM.md`
- Editor/URLs: `docs/modules/OVERLAY-EDITOR.md`
