import type { EffectInstance, LayoutItem } from "../types";
import { EFFECT_STRATEGIES } from "./registry";

const OUTER_FX = new Set(["glow", "glitch", "outline", "drop-shadow"]);

export type EffectTarget = "box" | "content";

export function effectTargets(strategy?: { targets?: EffectTarget[] } | null): EffectTarget[] {
  const listed = strategy?.targets;
  if (listed && listed.length) {
    return listed.filter((t): t is EffectTarget => t === "box" || t === "content");
  }
  return ["box"];
}

export function resolveEffectTarget(
  effect: EffectInstance,
  strategy?: { targets?: EffectTarget[] } | null
): EffectTarget {
  const allowed = effectTargets(strategy);
  if (effect.target === "content" && allowed.includes("content")) return "content";
  return "box";
}

function clearHostFx(host: HTMLElement): void {
  const stack = host.querySelector(":scope > .ccs-item-fx-stack");
  if (stack) stack.remove();

  host.classList.remove(
    "ccs-item-has-outer-fx",
    "ccs-item-has-glitch",
    "ccs-item-has-outline",
    "ccs-item-outline--pulse",
    "ccs-item-outline--breathe",
    "ccs-item-glow-content--pulse",
    "ccs-item-glow-content--breathe",
    "ccs-item-shadow-content--pulse",
    "ccs-item-shadow-content--breathe"
  );
  host.style.removeProperty("--ccs-fx-glow-color");
  host.style.removeProperty("--ccs-fx-glow-size");
  host.style.removeProperty("--ccs-fx-glow-spread");
  host.style.removeProperty("--ccs-fx-glow-speed");
  host.style.removeProperty("--ccs-fx-glow-i");
  host.style.removeProperty("--ccs-fx-glow-scale");
  host.style.removeProperty("--ccs-fx-shadow-speed");
  host.style.removeProperty("--ccs-fx-shadow-i");
  host.style.removeProperty("--ccs-fx-shadow-scale");
  host.style.removeProperty("--ccs-fx-glitch-intensity");
  host.style.removeProperty("--ccs-fx-glitch-speed");
  host.style.removeProperty("--ccs-fx-glitch-shift");
  host.style.removeProperty("--ccs-fx-outline-color");
  host.style.removeProperty("--ccs-fx-outline-width");
  host.style.removeProperty("--ccs-fx-outline-opacity");
  host.style.removeProperty("--ccs-fx-outline-speed");
  host.style.removeProperty("box-shadow");
  host.style.removeProperty("filter");
  host.style.removeProperty("outline");
  host.style.removeProperty("outline-offset");
}

export function applyItemEffects(wrapper: HTMLElement, item: LayoutItem): void {
  const content = wrapper.querySelector(':scope > [data-role="content"]') as HTMLElement | null;
  clearHostFx(wrapper);
  if (content) clearHostFx(content);

  const effects = item.effects || [];
  if (!effects.length) return;

  const boxStack = document.createElement("div");
  boxStack.className = "ccs-item-fx-stack";
  boxStack.dataset.fxTarget = "box";

  const contentStack = document.createElement("div");
  contentStack.className = "ccs-item-fx-stack";
  contentStack.dataset.fxTarget = "content";

  let boxOuter = false;
  let contentOuter = false;

  for (const effect of effects) {
    if (effect.enabled === false) continue;
    const strategy = EFFECT_STRATEGIES[effect.type];
    if (!strategy) continue;

    const target = resolveEffectTarget(effect, strategy);
    const host = target === "content" && content ? content : wrapper;
    if (OUTER_FX.has(effect.type)) {
      if (host === content) contentOuter = true;
      else boxOuter = true;
    }

    const layer = document.createElement("div");
    strategy.apply(layer, effect, item, host);
    if (layer.dataset.fxHost === "wrapper") {
      continue;
    }
    if (host === content) contentStack.appendChild(layer);
    else boxStack.appendChild(layer);
  }

  if (boxOuter) wrapper.classList.add("ccs-item-has-outer-fx");
  if (contentOuter && content) {
    content.classList.add("ccs-item-has-outer-fx");
    wrapper.classList.add("ccs-item-has-outer-fx");
  }

  if (boxStack.children.length) {
    wrapper.appendChild(boxStack);
  }
  if (content && contentStack.children.length) {
    const cs = getComputedStyle(content);
    if (cs.position === "static") content.style.position = "relative";
    content.appendChild(contentStack);
  }
}
