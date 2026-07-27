import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";

function setting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const settings = effect.settings || {};
  if (settings[key] != null) return settings[key];
  if (effect[key] != null) return effect[key];
  return fallback;
}

export const scanlinesStrategy: EffectStrategy = {
  type: "scanlines",
  label: "Scanlines",
  defaults: { opacity: 0.06 },
  fields: [
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.06, step: 0.01 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const opacity = Number(setting(effect, "opacity", 0.06));
    layer.className = "ccs-item-fx-layer ccs-item-fx-scanlines";
    layer.style.setProperty("--ccs-fx-scanline-opacity", String(opacity));
  }
};

export function applyScanlinesEffect(layer: HTMLElement, effect: EffectInstance, item: LayoutItem): void {
  scanlinesStrategy.apply(layer, effect, item);
}
