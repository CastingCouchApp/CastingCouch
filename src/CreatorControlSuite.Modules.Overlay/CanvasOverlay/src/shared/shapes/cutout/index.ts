import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./cutout.css";

export function applyCutout(el: HTMLElement, item: LayoutItem): void {
  let radius = Number(prop(item, "radius", 24));
  if (!Number.isFinite(radius)) radius = 24;
  radius = Math.max(0, radius);
  el.style.setProperty("--cutout-radius", radius + "px");
}

export function createCutoutEl(item: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className = "ccs-shape ccs-shape-cutout";
  applyCutout(el, item);
  return el;
}
