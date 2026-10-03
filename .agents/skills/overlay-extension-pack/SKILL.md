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


## Tauri-Backend

Tauri verwendet dieselben HTTP-Routen und dasselbe gemeinsame Canvas-Frontend ohne .NET-Sidecar. ZIP-Extraktion, Austausch mit Rücknahme, Katalog und Deinstallation liegen in `tauri-app/src-tauri/crates/ccs-overlay-server/src/library.rs`; Manifestprüfung und Normalisierung in `pack_manifest.rs`, HTTP und Multipart in `routes.rs`. Änderungen an Pack-Manifest oder Dateiregeln mit dem C#-Host abgleichen und in `tests/library.rs` sowie `tests/extension_http.rs` absichern. Höchstgröße: 50 MB komprimiert und entpackt. Fehlerhafte Updates müssen das bestehende Pack erhalten. Einträge mit Traversal, Symlinks, unerlaubten Endungen oder doppelten Namen (auch nur durch Großschreibung verschieden) ablehnen.

Native Commands und HTTP verwenden den gemeinsamen `extension_service.rs`. Die App importiert über den nativen ZIP-Dateidialog und `list_extension_packs`, `import_extension_pack(path)`, `uninstall_extension_pack(id)` in `tauri-app/src-tauri/src/lib.rs`. Nach erfolgreichen Änderungen veröffentlicht der Service das kompatible WebSocket-Ereignis und der Host `extension-packs-changed`; die UI lädt daraufhin den Katalog erneut. Fehlgeschlagene Imports und Dialogabbruch dürfen keinen Erfolg melden. Änderungen an Commands erfordern die generierte `tauri-app/src/lib/command-contract.ts` (`npm run contracts:generate`), Ereignisse den typisierten Helper in `api.ts`. Den tatsächlichen IPC-/HTTP-/Ereignisvertrag in `src-tauri/src/command_tests.rs` und Dialog/Abbruch/Katalogaktualisierung in `src/features/overlay/OverlayLibrary.test.tsx` mitprüfen.

- C#-Manifestfelder werden ohne Beachtung der Großschreibung gelesen; fehlende oder `null`-Listen werden zu leeren Listen. Unbekannte Zusatzfelder bleiben erhalten. Mehrere unterschiedlich geschriebene Varianten desselben bekannten Felds sind mehrdeutig und werden abgelehnt.
- ZIP-Pfade und Manifestverweise mit Windows-Backslashes werden zu `/` normalisiert und anschließend geprüft. Referenzen übernehmen die tatsächliche Schreibweise der Archivdatei, damit sie auch auf macOS funktionieren. `manifest.json` liegt nach Installation unter diesem kanonischen Namen.
- Widgets/Effects/Animations benötigen `id`, `name`, `entry`; Fonts `family`, `src`. Referenzierte JS-/CSS-/Font-/Asset-Dateien müssen existieren. JS-Einstiege benötigen `.js`, Styles `.css`, Fonts `.woff2/.woff/.ttf/.otf`. Diese zusätzlichen Referenzprüfungen vermeiden scheinbar erfolgreiche Installationen unbrauchbarer Packs.
- Kataloge überspringen wie C# beschädigte Packs und sortieren nach Namen; übrige Packs bleiben nutzbar. Bei manueller Reparatur kann die App den Katalog erneut laden.
- Der gemeinsame Loader verwendet passende Fontformate (`woff2`, `woff`, `truetype`, `opentype`). Den bestehenden C#-Fixture-Inhalt mit dem echten HTTP-Test und `CanvasOverlay/tests/extension-fixture.test.ts` prüfen: Originalskripte per HTTP laden und ausführen, Registrierung, Widget-Update, Effekt/Animation, Palette und Font-/Asset-URLs prüfen. JSDOM bestätigt keine tatsächliche Fontdarstellung oder OBS-CEF-/Installationsabnahme.
