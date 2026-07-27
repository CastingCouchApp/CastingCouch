import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";

function setting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const settings = effect.settings || {};
  if (settings[key] != null) return settings[key];
  if (effect[key] != null) return effect[key];
  return fallback;
}

export const noiseStrategy: EffectStrategy = {
  type: "noise",
  label: "Noise",
  defaults: { opacity: 0.08 },
  fields: [
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.08, step: 0.01 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const opacity = Number(setting(effect, "opacity", 0.08));
    layer.className = "ccs-item-fx-layer ccs-item-fx-noise";
    layer.style.setProperty("--ccs-fx-noise-opacity", String(opacity));
  }
};

export function applyNoiseEffect(layer: HTMLElement, effect: EffectInstance, item: LayoutItem): void {
  noiseStrategy.apply(layer, effect, item);
}
