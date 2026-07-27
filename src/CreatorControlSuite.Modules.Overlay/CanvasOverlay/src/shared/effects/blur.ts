import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";

function setting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const settings = effect.settings || {};
  if (settings[key] != null) return settings[key];
  if (effect[key] != null) return effect[key];
  return fallback;
}

export const blurStrategy: EffectStrategy = {
  type: "blur",
  label: "Blur",
  defaults: { amount: 4 },
  fields: [
    { key: "amount", kind: "number", label: "Blur px", fallback: 4, step: 0.5 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const amount = Number(setting(effect, "amount", 4));
    layer.className = "ccs-item-fx-layer ccs-item-fx-blur";
    layer.style.setProperty("--ccs-fx-blur-amount", amount + "px");
    layer.style.backdropFilter = `blur(${amount}px)`;
    (layer.style as CSSStyleDeclaration & { webkitBackdropFilter?: string }).webkitBackdropFilter = `blur(${amount}px)`;
  }
};

export function applyBlurEffect(layer: HTMLElement, effect: EffectInstance, item: LayoutItem): void {
  blurStrategy.apply(layer, effect, item);
}
