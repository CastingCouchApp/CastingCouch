import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";

function setting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const settings = effect.settings || {};
  if (settings[key] != null) return settings[key];
  if (effect[key] != null) return effect[key];
  return fallback;
}

export const vignetteStrategy: EffectStrategy = {
  type: "vignette",
  label: "Vignette",
  defaults: { strength: 0.45 },
  fields: [
    { key: "strength", kind: "number", label: "Stärke", fallback: 0.45, step: 0.05 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const strength = Number(setting(effect, "strength", 0.45));
    layer.className = "ccs-item-fx-layer ccs-item-fx-vignette";
    layer.style.setProperty("--ccs-fx-vignette-strength", String(strength));
  }
};

export function applyVignetteEffect(layer: HTMLElement, effect: EffectInstance, item: LayoutItem): void {
  vignetteStrategy.apply(layer, effect, item);
}
