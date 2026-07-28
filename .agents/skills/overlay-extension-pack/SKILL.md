---
name: overlay-extension-pack
description: >-
  Erzeugt oder integriert ein Overlay-Extension-Pack (.zip) mit Custom Widgets,
  Effects, Animations, Webfonts, SVGs und Images: manifest.json, Ordnerstruktur,
  registerWidget/registerEffect/registerAnimation, extUrl, Installation in der App.
  Nutzen bei Extension Pack, Overlay ZIP, Custom Widget Pack, Webfont Pack,
  SVG/Image Assets, ext:-Typen, Pack-Manifest.
---

# Overlay Extension Pack

Packs erweitern Canvas **ohne App-Rebuild**. Installation lokal über Overlay-Seite
oder `POST /extensions/install` (Loopback).

## Fortschritt

```
Overlay Extension Pack Progress:
- [ ] 1. Pack-ID, Inhalt (widgets/effects/animations/fonts/assets) klären
- [ ] 2. manifest.json (apiVersion 1) + Ordnerstruktur
- [ ] 3. Module gegen registerWidget/registerEffect/registerAnimation + extUrl
- [ ] 4. Fonts + SVG/Images unter assets/
- [ ] 5. ZIP bauen; Allowlist/Pfade prüfen
- [ ] 6. In App installieren; /extensions + /ext/{id}/ smoke-testen
- [ ] 7. Editor: Widget-Palette + Effekt-/Animations-Dropdowns; Layout speichert ext:…
- [ ] 8. Docs + Fixture/Tests falls Core-API geändert
```

## Pack-Layout

```
cool-kit.zip
  manifest.json
  widgets/banner/index.js
  effects/sparkle/index.js
  animations/wobble/index.js
  fonts/CoolFont.woff2
  assets/icons/logo.svg
```

### manifest.json (apiVersion 1)

```json
{
  "id": "cool-kit",
  "name": "Cool Kit",
  "version": "1.0.0",
  "apiVersion": 1,
  "widgets": [
    { "id": "banner", "name": "Banner", "entry": "widgets/banner/index.js" }
  ],
  "effects": [
    { "id": "sparkle", "name": "Sparkle", "entry": "effects/sparkle/index.js" }
  ],
  "animations": [
    { "id": "wobble", "name": "Wobble", "entry": "animations/wobble/index.js" }
  ],
  "fonts": [
    { "family": "CoolFont", "src": "fonts/CoolFont.woff2", "weight": "400", "style": "normal" }
  ],
  "assets": ["assets/icons/logo.svg"]
}
```

- Runtime-Typen: `ext:{packId}:{id}` (z. B. `ext:cool-kit:banner`)
- Assets: `/ext/{packId}/…` bzw. `CcsCanvas.extUrl(packId, relativePath)`
- Allowlist: `.js .css .woff2 .woff .ttf .otf .svg .png .jpg .jpeg .webp .gif .json .md`

## Modul-API

```js
CcsCanvas.registerWidget("ext:cool-kit:banner", {
  defaults: { w: 400, h: 120, props: {} },
  create(item) { /* DOM */ },
  update(el, item, data) { /* optional */ }
});

CcsCanvas.registerEffect("ext:cool-kit:sparkle", {
  label: "Sparkle",
  defaults: { intensity: 0.5 },
  fields: [{ key: "intensity", kind: "number", label: "Intensität" }],
  apply(layer, effect, item) { /* … */ }
});

CcsCanvas.registerAnimation("ext:cool-kit:wobble", {
  label: "Wobble",
  defaults: { intensity: 0.6 },
  fields: [{ key: "intensity", kind: "number", label: "Intensität" }],
  apply(el, animation, item) { /* … */ }
});
```

Katalog: `GET /extensions` → `{ packs: [...] }`. Loader lädt Widgets/Effects/Animations + Fonts
beim Boot per **fetch + Inline-Inject** (nicht via `<script src>`/`<link>`-onload — das hängt in OBS-CEF).

**Editor:** Nach `loadExtensions()` erscheinen Pack-Widgets unter `Extension · {Pack-Name}` in der linken Palette. Pack-Effekte/-Animationen landen über `registerEffect`/`registerAnimation` in den Inspector-Dropdowns (`listEffectTypes` / `listAnimationTypes`).

## Host

| Komponente | Rolle |
|------------|--------|
| `OverlayExtensionStore` | ZIP install/validate/extract |
| `OverlayWebServer` | `/extensions`, `/ext/{id}/*` |
| Overlay-Seite | Import / Deinstallieren |
| Editor `pack-palette.ts` | Pack-Widgets in Palette mergen |

Root: `%LocalAppData%\CreatorControlSuite\Overlay\extensions\{packId}\`

## Anti-Patterns

- Typ ohne `ext:packId:`-Prefix
- Absolute Disk-Pfade in Pack-JS
- Nicht erlaubte Dateitypen / Zip-Slip
- Builtin-Whitelist statt Pack für Community-Content
- Pack-Widgets nur in Manifest, aber nicht per `registerWidget("ext:…")` registriert

## Fixture

`tests/CreatorControlSuite.Tests/Fixtures/overlay-pack/cool-kit/`
