import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./cutout.css";

const SVG_NS = "http://www.w3.org/2000/svg";

export function cutoutRadius(item: LayoutItem): number {
  let radius = Number(prop(item, "radius", 24));
  if (!Number.isFinite(radius)) radius = 24;
  return Math.max(0, radius);
}

export function applyCutout(el: HTMLElement, item: LayoutItem): void {
  el.style.setProperty("--cutout-radius", cutoutRadius(item) + "px");
}

export function createCutoutEl(item: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className = "ccs-shape ccs-shape-cutout";
  applyCutout(el, item);
  return el;
}

export function ensureCutoutSvg(canvas: HTMLElement): SVGSVGElement {
  let svg = canvas.querySelector(":scope > svg.ccs-cutout-defs") as SVGSVGElement | null;
  if (svg) return svg;

  svg = document.createElementNS(SVG_NS, "svg") as SVGSVGElement;
  svg.classList.add("ccs-cutout-defs");
  svg.setAttribute("width", "0");
  svg.setAttribute("height", "0");
  svg.setAttribute("aria-hidden", "true");
  svg.style.position = "absolute";
  svg.style.pointerEvents = "none";
  const defs = document.createElementNS(SVG_NS, "defs");
  svg.appendChild(defs);
  canvas.insertBefore(svg, canvas.firstChild);
  return svg;
}

/**
 * Wraps already-painted canvas content (below this cutout) into a masked stack
 * so the cutout region becomes true alpha transparency (OBS-safe).
 */
export function applyCutoutStackMask(
  canvas: HTMLElement,
  item: LayoutItem,
  seq: number,
  canvasWidth: number,
  canvasHeight: number
): HTMLElement | null {
  const move: Element[] = [];
  for (const child of Array.from(canvas.children)) {
    if (child.classList.contains("ccs-cutout-defs")) continue;
    move.push(child);
  }
  if (move.length === 0) return null;

  const stack = document.createElement("div");
  stack.className = "ccs-cutout-stack";
  for (const node of move) {
    stack.appendChild(node);
  }

  const cw = Math.max(1, canvasWidth || 1920);
  const ch = Math.max(1, canvasHeight || 1080);
  const svg = ensureCutoutSvg(canvas);
  const defs = svg.querySelector("defs");
  if (!defs) return null;

  const id = `ccs-cutout-mask-${seq}`;
  const prev = defs.querySelector("#" + id);
  if (prev) prev.remove();

  const mask = document.createElementNS(SVG_NS, "mask");
  mask.setAttribute("id", id);
  mask.setAttribute("maskUnits", "userSpaceOnUse");
  mask.setAttribute("mask-type", "luminance");
  mask.setAttribute("x", "0");
  mask.setAttribute("y", "0");
  mask.setAttribute("width", String(cw));
  mask.setAttribute("height", String(ch));

  const full = document.createElementNS(SVG_NS, "rect");
  full.setAttribute("x", "0");
  full.setAttribute("y", "0");
  full.setAttribute("width", String(cw));
  full.setAttribute("height", String(ch));
  full.setAttribute("fill", "#ffffff");
  mask.appendChild(full);

  const hole = document.createElementNS(SVG_NS, "rect");
  hole.setAttribute("x", String(item.x || 0));
  hole.setAttribute("y", String(item.y || 0));
  hole.setAttribute("width", String(Math.max(0, item.w || 0)));
  hole.setAttribute("height", String(Math.max(0, item.h || 0)));
  const radius = cutoutRadius(item);
  hole.setAttribute("rx", String(radius));
  hole.setAttribute("ry", String(radius));
  hole.setAttribute("fill", "#000000");
  mask.appendChild(hole);
  defs.appendChild(mask);

  const ref = `url(#${id})`;
  stack.style.setProperty("mask-image", ref);
  stack.style.setProperty("-webkit-mask-image", ref);
  stack.style.setProperty("mask-mode", "luminance");
  stack.style.setProperty("-webkit-mask-repeat", "no-repeat");
  stack.style.setProperty("mask-repeat", "no-repeat");
  stack.style.setProperty("-webkit-mask-size", "100% 100%");
  stack.style.setProperty("mask-size", "100% 100%");

  canvas.appendChild(stack);
  return stack;
}
