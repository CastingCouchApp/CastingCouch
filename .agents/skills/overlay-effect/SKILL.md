---
name: overlay-effect
description: >-
  Führt einen Built-in Canvas-Overlay-Effect-Modifier ein (glow, particles,
  scanlines, vignette, blur, noise, …): Strategy unter
  CanvasOverlay/src/shared/effects/<type>/, Registry, CSS-Layer, Editor-fields,
  Tests. Nutzen bei Glow, Particles, Scanlines, Vignette, Blur, Noise,
  Effect-Modifier, LiveFX, item.effects, Strategy. Pack-Effects →
  overlay-extension-pack.
---

# Overlay-Effect einführen (Built-in)

Effects sind stapelbare Modifier auf **allen** Layout-Items (`item.effects[]`).
Optional `target: "box" | "content"` — nur anbieten, wenn `strategy.targets` `content` enthält
(Default ohne Angabe: nur `box`). Content-fähig u. a. Glow, Drop Shadow, Outline, Glitch.
Built-ins liegen unter `CanvasOverlay/src/shared/effects/<type>/`.

## Fortschritt

```
Overlay Effect Progress:
- [ ] 1. type-ID, defaults, settings-fields klären
- [ ] 2. Tests (rot): EFFECT_STRATEGIES, .ccs-fx-<type>, Bundle-Marker
- [ ] 3. shared/effects/<type>/ (defaults/fields in Strategy, apply, css) + registry.ts
- [ ] 4. fx-base / Layer-Konvention (.ccs-item-fx-layer, pointer-events: none)
- [ ] 5. Kein Editor-Fork — strategy.fields → Effects-Panel
- [ ] 6. npm run build
- [ ] 7. Docs (OVERLAY-SYSTEM Effect-Tabelle)
- [ ] 8. Tests grün
```

## Strategy-Schnittstelle

```ts
{
  type: "glow",
  label: "Glow",
  defaults: { color: "#ff7a00", blur: 28, intensity: 0.55 },
  fields: [
    { key: "color", kind: "color", label: "Farbe" },
    { key: "blur", kind: "number", label: "Blur" }
  ],
  apply(layer, effect, item) { /* CSS vars / classes */ }
}
```

Registrierung in `effects/registry.ts`. Runtime: `applyItemEffects`. Editor: generisches Effects-Panel.

Ausgeliefert: `glow`, `particles`, `scanlines`, `vignette`, `blur`, `noise`,
`neon`, `glitch`, `sparkle`, `aurora`, `pulse-ring`, `hologram`, `outline`,
`drop-shadow`, `rainbow`, `spotlight`.

Item-Animationen (`item.animations[]`) liegen parallel unter
`shared/animations/` (`fade`, `slide`, `bounce`, `pop`, `shake`, `float`,
`pulse`, `spin`, `wiggle`, `flip`) — gleiches Strategy-/Panel-Muster.

## Anti-Patterns

- Effekt nur in einem Widget hardcoden
- Eigenen `if` im Editor für Settings
- CSS ohne `pointer-events: none` auf FX-Layern

## Packs

Custom/community Effects als ZIP → Skill `overlay-extension-pack` (`ext:{packId}:{type}`).
