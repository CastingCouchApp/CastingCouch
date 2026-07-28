import type { LayoutItem } from "../types";
import { ANIMATION_STRATEGIES } from "./registry";

const ANIM_CLASS_RE = /^ccs-item-anim/;

export function applyItemAnimations(wrapper: HTMLElement, item: LayoutItem): void {
  const target = wrapper.querySelector('[data-role="content"]') as HTMLElement | null;
  if (!target) return;

  const toRemove: string[] = [];
  target.classList.forEach((cls) => {
    if (ANIM_CLASS_RE.test(cls)) toRemove.push(cls);
  });
  for (const cls of toRemove) target.classList.remove(cls);
  target.classList.remove("ccs-item-anim-target");
  target.style.removeProperty("animation");
  target.style.removeProperty("--ccs-anim-fade-min");
  target.style.removeProperty("--ccs-anim-slide-x");
  target.style.removeProperty("--ccs-anim-slide-y");
  target.style.removeProperty("--ccs-anim-bounce-h");
  target.style.removeProperty("--ccs-anim-pop-scale");
  target.style.removeProperty("--ccs-anim-shake");
  target.style.removeProperty("--ccs-anim-float");
  target.style.removeProperty("--ccs-anim-pulse-scale");
  target.style.removeProperty("--ccs-anim-wiggle");

  const animations = item.animations || [];
  if (!animations.length) return;

  const parts: string[] = [];
  for (const animation of animations) {
    if (animation.enabled === false) continue;
    const strategy = ANIMATION_STRATEGIES[animation.type];
    if (!strategy) continue;
    const shorthand = strategy.apply(target, animation, item);
    if (shorthand) parts.push(shorthand);
  }

  if (parts.length) {
    target.classList.add("ccs-item-anim-target");
    target.style.animation = parts.join(", ");
  }
}
