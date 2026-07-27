import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { rgbaFrom } from "../utils/color";

function setting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const settings = effect.settings || {};
  if (settings[key] != null) return settings[key];
  if (effect[key] != null) return effect[key];
  return fallback;
}

export const glowStrategy: EffectStrategy = {
  type: "glow",
  label: "Glow",
  defaults: { color: "#ff7a00", blur: 28, intensity: 0.7 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#ff7a00" },
    { key: "blur", kind: "number", label: "Blur", fallback: 28 },
    { key: "intensity", kind: "number", label: "Intensität", fallback: 0.7, step: 0.05 }
  ],
  apply(layer: HTMLElement, effect: EffectInstance, _item: LayoutItem, wrapper?: HTMLElement): void {
    const color = String(setting(effect, "color", "#ff7a00"));
    const blur = Math.max(0, Number(setting(effect, "blur", setting(effect, "size", 28))));
    const intensity = Math.min(1, Math.max(0, Number(setting(effect, "intensity", setting(effect, "opacity", 0.7)))));
    const core = rgbaFrom(color, intensity);
    const soft = rgbaFrom(color, intensity * 0.55);
    const halo = rgbaFrom(color, intensity * 0.28);
    const shadow =
      `0 0 ${Math.max(2, blur * 0.25)}px ${core}, ` +
      `0 0 ${blur}px ${soft}, ` +
      `0 0 ${blur * 2}px ${halo}`;

    // Outer glow am Item-Wrapper — sonst clippt overflow:hidden den box-shadow.
    layer.dataset.fxHost = "wrapper";
    if (wrapper) {
      wrapper.style.setProperty("--ccs-fx-glow-color", core);
      wrapper.style.setProperty("--ccs-fx-glow-size", blur + "px");
      const existing = wrapper.style.boxShadow;
      wrapper.style.boxShadow = existing ? existing + ", " + shadow : shadow;
    }
  }
};

export function applyGlowEffect(layer: HTMLElement, effect: EffectInstance, item: LayoutItem): void {
  glowStrategy.apply(layer, effect, item);
}
