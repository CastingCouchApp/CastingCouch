import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import {
  FRAME_MODES,
  LEGACY_FRAME_TYPE_TO_MODE,
  type FrameMode
} from "../../defaults/shapes";
import "./frame.css";

const MODE_SET = new Set<string>(FRAME_MODES);

export function resolveFrameMode(item: LayoutItem | null | undefined): FrameMode {
  const type = (item && item.type) || "";
  const raw = prop(item, "mode", null);
  if (raw != null && String(raw).trim() !== "") {
    let mode = String(raw).toLowerCase();
    if (MODE_SET.has(mode)) return mode as FrameMode;
  }
  const legacy = LEGACY_FRAME_TYPE_TO_MODE[type];
  if (legacy) return legacy;
  return "rect";
}

export function isUnifiedFrameType(type: string | null | undefined): boolean {
  const t = type || "";
  return t === "frame" || !!LEGACY_FRAME_TYPE_TO_MODE[t];
}

export function applyFrame(el: HTMLElement, item: LayoutItem): void {
  const mode = resolveFrameMode(item);
  const color = String(prop(item, "color", "#ff7a00") || "#ff7a00");
  let radius = Number(prop(item, "radius", 16));
  if (!Number.isFinite(radius)) radius = 16;
  radius = Math.max(0, radius);

  el.className = `ccs-shape ccs-frame ccs-frame-m-${mode}`;
  el.dataset.mode = mode;
  el.style.setProperty("--frame-color", color);
  el.style.setProperty("--frame-radius", radius + "px");

  ensureOrbitDots(el, mode);
}

export function createFrameEl(item: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  applyFrame(el, item);
  return el;
}

function ensureOrbitDots(el: HTMLElement, mode: FrameMode): void {
  const existing = el.querySelector(".ccs-frame-orbit-dots");
  if (mode !== "orbit") {
    if (existing) existing.remove();
    return;
  }
  if (existing) return;
  const dots = document.createElement("div");
  dots.className = "ccs-frame-orbit-dots";
  for (let i = 0; i < 4; i++) {
    const dot = document.createElement("span");
    dot.className = "ccs-frame-orbit-dot";
    dot.style.setProperty("--i", String(i));
    dots.appendChild(dot);
  }
  el.appendChild(dots);
}
