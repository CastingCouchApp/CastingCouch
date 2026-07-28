import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./cam-ring.css";

export const CAM_RING_VARIANTS = [
  "ring",
  "double-ring",
  "hex",
  "soft",
  "neon",
  "cyber",
  "pixel",
  "dashed",
  "corners",
  "badge-only",
  "orbit",
  "square-round"
] as const;

export type CamRingVariant = (typeof CAM_RING_VARIANTS)[number];

export const CAM_RING_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  "cam-sm": { w: 240, h: 240, label: "Cam SM" },
  "cam-md": { w: 360, h: 360, label: "Cam MD" },
  "cam-lg": { w: 480, h: 480, label: "Cam LG" },
  "cam-tall": { w: 320, h: 420, label: "Cam Tall" }
};

const BADGE_LABELS: Record<string, string> = {
  live: "LIVE",
  rec: "REC",
  custom: ""
};

function camVariant(item: LayoutItem | null | undefined): CamRingVariant {
  const raw = String(prop(item, "variant", "ring") || "ring").toLowerCase();
  return (CAM_RING_VARIANTS as readonly string[]).includes(raw) ? (raw as CamRingVariant) : "ring";
}

function ensureOrbitDots(el: HTMLElement, show: boolean): void {
  let dots = el.querySelector<HTMLElement>(".ccs-cam-ring-orbit");
  if (!show) {
    dots?.remove();
    return;
  }
  if (dots) return;
  dots = document.createElement("div");
  dots.className = "ccs-cam-ring-orbit";
  for (let i = 0; i < 3; i++) {
    const dot = document.createElement("span");
    dot.className = "ccs-cam-ring-orbit-dot";
    dot.style.setProperty("--i", String(i));
    dots.appendChild(dot);
  }
  el.appendChild(dots);
}

function ensureBadge(el: HTMLElement): HTMLElement {
  let badge = el.querySelector<HTMLElement>(".ccs-cam-ring-badge");
  if (!badge) {
    badge = document.createElement("span");
    badge.className = "ccs-cam-ring-badge";
    el.appendChild(badge);
  }
  return badge;
}

export function applyCamRing(el: HTMLElement, item: LayoutItem): void {
  const variant = camVariant(item);
  CAM_RING_VARIANTS.forEach((name) => el.classList.remove("ccs-cam-ring-v-" + name));
  el.classList.add("ccs-cam-ring-v-" + variant);
  el.dataset.variant = variant;

  const sizeKey = String(prop(item, "sizePreset", "cam-md") || "cam-md").toLowerCase();
  Object.keys(CAM_RING_SIZE_PRESETS).forEach((name) => el.classList.remove("ccs-cam-ring-s-" + name));
  if (CAM_RING_SIZE_PRESETS[sizeKey]) {
    el.classList.add("ccs-cam-ring-s-" + sizeKey);
  }

  const strokeWidth = Math.max(1, Number(prop(item, "strokeWidth", 4)) || 4);
  const gap = Math.max(0, Number(prop(item, "gap", 6)) || 6);
  const radius = Math.max(0, Number(prop(item, "radius", 50)) || 50);
  const color = String(prop(item, "color", "#ff7a00") || "#ff7a00");
  const color2 = String(prop(item, "color2", "#ffffff") || "#ffffff");
  const pulse = prop(item, "pulse", false) === true;
  const rotateSlow = prop(item, "rotateSlow", false) === true;
  const showInnerGlow = prop(item, "showInnerGlow", true) !== false;

  el.style.setProperty("--ccs-cam-stroke", strokeWidth + "px");
  el.style.setProperty("--ccs-cam-gap", gap + "px");
  el.style.setProperty("--ccs-cam-radius", radius + "%");
  el.style.setProperty("--ccs-cam-color", color);
  el.style.setProperty("--ccs-cam-color2", color2);

  el.classList.toggle("ccs-cam-pulse", pulse);
  el.classList.toggle("ccs-cam-rotate", rotateSlow);
  el.classList.toggle("ccs-cam-inner-glow", showInnerGlow);

  const badgeMode = String(prop(item, "badge", "none") || "none").toLowerCase();
  const badge = ensureBadge(el);
  const badgePos = String(prop(item, "badgePosition", "tr") || "tr").toLowerCase();
  badge.className = "ccs-cam-ring-badge ccs-cam-badge-pos-" + badgePos;

  if (badgeMode === "none" || variant === "badge-only" && badgeMode === "none") {
    badge.style.display = "none";
  } else {
    badge.style.display = "";
    const customText = String(prop(item, "badgeText", "") || "").trim();
    badge.textContent = customText || BADGE_LABELS[badgeMode] || badgeMode.toUpperCase();
    badge.dataset.mode = badgeMode;
    badge.classList.toggle("is-live", badgeMode === "live");
    badge.classList.toggle("is-rec", badgeMode === "rec");
  }

  ensureOrbitDots(el, variant === "orbit");
}

export function createCamRingEl(item: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className = "ccs-shape ccs-cam-ring ccs-cam-ring-v-ring";
  el.innerHTML = `<span class="ccs-cam-ring-stroke" aria-hidden="true"></span>`;
  applyCamRing(el, item);
  return el;
}

export function updateCamRing(el: HTMLElement, item: LayoutItem): void {
  applyCamRing(el, item);
}
