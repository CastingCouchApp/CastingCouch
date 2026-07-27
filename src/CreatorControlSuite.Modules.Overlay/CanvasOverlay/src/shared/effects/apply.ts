import type { EffectInstance, LayoutItem } from "../types";
import { EFFECT_STRATEGIES } from "./registry";

const OUTER_FX = new Set(["glow"]);

export function applyItemEffects(wrapper: HTMLElement, item: LayoutItem): void {
  const stack = wrapper.querySelector(".ccs-item-fx-stack");
  if (stack) stack.remove();

  wrapper.classList.remove("ccs-item-has-outer-fx");
  wrapper.style.removeProperty("--ccs-fx-glow-color");
  wrapper.style.removeProperty("--ccs-fx-glow-size");
  wrapper.style.removeProperty("--ccs-fx-glow-spread");
  wrapper.style.removeProperty("box-shadow");

  const effects = item.effects || [];
  if (!effects.length) return;

  const fxStack = document.createElement("div");
  fxStack.className = "ccs-item-fx-stack";

  let hasOuter = false;
  for (const effect of effects) {
    if (effect.enabled === false) continue;
    const strategy = EFFECT_STRATEGIES[effect.type];
    if (!strategy) continue;
    if (OUTER_FX.has(effect.type)) {
      hasOuter = true;
    }
    const layer = document.createElement("div");
    strategy.apply(layer, effect, item, wrapper);
    if (layer.dataset.fxHost === "wrapper") {
      continue;
    }
    fxStack.appendChild(layer);
  }

  if (hasOuter) {
    wrapper.classList.add("ccs-item-has-outer-fx");
  }

  if (fxStack.children.length) {
    wrapper.appendChild(fxStack);
  }
}
