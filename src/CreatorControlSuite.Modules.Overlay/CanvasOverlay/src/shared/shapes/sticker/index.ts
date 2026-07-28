import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./sticker.css";

export const STICKER_PRESETS = [
  "heart",
  "star",
  "fire",
  "sparkle",
  "crown",
  "skull",
  "controller",
  "chat-bubble",
  "raid",
  "bit",
  "thumbs-up",
  "custom"
] as const;

export type StickerPreset = (typeof STICKER_PRESETS)[number];

export const STICKER_VARIANTS = ["flat", "neon", "glass", "outline", "soft", "badge"] as const;
export type StickerVariant = (typeof STICKER_VARIANTS)[number];

const PRESET_SVG: Record<Exclude<StickerPreset, "custom">, string> = {
  heart:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/></svg>',
  star:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"/></svg>',
  fire:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M13.5.67s.74 2.65.74 4.8c0 2.49-2.06 4.5-4.6 4.5-1.02 0-1.96-.33-2.72-.89.3 2.38 1.86 4.42 4.02 5.29C8.5 18.1 10.62 20 13 20c3.31 0 6-2.69 6-6 0-4.5-5.5-12.83-5.5-12.83z"/></svg>',
  sparkle:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 0l2.4 7.2L22 9.6l-7.2 2.4L12 19.2 9.2 12 2 9.6l7.6-.4L12 0zm0 14.4l1.2 3.6 3.6 1.2-3.6 1.2L12 24l-1.2-3.6-3.6-1.2 3.6-1.2L12 14.4z"/></svg>',
  crown:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M5 16L3 5l5.5 5L12 4l3.5 6L21 5l-2 11H5zm14 3H5v2h14v-2z"/></svg>',
  skull:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C8.13 2 5 5.13 5 9c0 2.38 1.19 4.47 3 5.74V17c0 .55.45 1 1 1h1v3h8v-3h1c.55 0 1-.45 1-1v-2.26c1.81-1.27 3-3.36 3-5.74 0-3.87-3.13-7-7-7zm-2 13v2h4v-2h-4zm0-4a1.5 1.5 0 110-3 1.5 1.5 0 010 3zm4 0a1.5 1.5 0 110-3 1.5 1.5 0 010 3z"/></svg>',
  controller:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-10 7H8v2H6v-2H4v-2h2V9h2v2h3v2zm4 .5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm4 0c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z"/></svg>',
  "chat-bubble":
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2z"/></svg>',
  raid:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3zm-1 14.5h2v2h-2v-2zm0-8h2v6h-2v-6z"/></svg>',
  bit:
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 15h-2v-6h2v6zm4 0h-2v-6h2v6z"/></svg>',
  "thumbs-up":
    '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M1 21h4V9H1v12zm22-11c0-1.1-.9-2-2-2h-6.31l.95-4.57.03-.32c0-.41-.17-.79-.44-1.06L14.17 1 7.59 7.59C7.22 7.95 7 8.45 7 9v10c0 1.1.9 2 2 2h9c.83 0 1.54-.5 1.84-1.22l3.02-7.05c.09-.23.14-.47.14-.73v-2z"/></svg>'
};

function stickerPreset(item: LayoutItem | null | undefined): StickerPreset {
  const raw = String(prop(item, "preset", "heart") || "heart").toLowerCase();
  return (STICKER_PRESETS as readonly string[]).includes(raw) ? (raw as StickerPreset) : "heart";
}

function stickerVariant(item: LayoutItem | null | undefined): StickerVariant {
  const raw = String(prop(item, "variant", "flat") || "flat").toLowerCase();
  return (STICKER_VARIANTS as readonly string[]).includes(raw) ? (raw as StickerVariant) : "flat";
}

function renderPresetGraphic(el: HTMLElement, preset: StickerPreset, src: string, fit: string): void {
  let graphic = el.querySelector<HTMLElement>(".ccs-sticker-graphic");
  if (!graphic) {
    graphic = document.createElement("div");
    graphic.className = "ccs-sticker-graphic";
    el.appendChild(graphic);
  }

  if (preset === "custom" && src) {
    graphic.innerHTML = `<img class="ccs-sticker-img" src="${src}" alt="" draggable="false" />`;
    const img = graphic.querySelector<HTMLImageElement>(".ccs-sticker-img");
    if (img) img.style.objectFit = fit;
    return;
  }

  const svg = PRESET_SVG[preset as Exclude<StickerPreset, "custom">] || PRESET_SVG.heart;
  graphic.innerHTML = `<span class="ccs-sticker-svg">${svg}</span>`;
}

export function applySticker(el: HTMLElement, item: LayoutItem): void {
  const variant = stickerVariant(item);
  STICKER_VARIANTS.forEach((name) => el.classList.remove("ccs-sticker-v-" + name));
  el.classList.add("ccs-sticker-v-" + variant);
  el.dataset.variant = variant;

  const preset = stickerPreset(item);
  el.dataset.preset = preset;

  const fit = String(prop(item, "fit", "contain") || "contain");
  const src = String(prop(item, "src", "") || "").trim();
  const rotateDeg = Number(prop(item, "rotateDeg", 0)) || 0;
  const scale = Math.max(0.1, Number(prop(item, "scale", 1)) || 1);
  const opacity = Math.max(0, Math.min(1, Number(prop(item, "opacity", 1)) || 1));
  const flipX = prop(item, "flipX", false) === true;
  const flipY = prop(item, "flipY", false) === true;
  const color = String(prop(item, "color", "#ff7a00") || "#ff7a00");
  const color2 = String(prop(item, "color2", "#ffffff") || "#ffffff");

  el.style.setProperty("--ccs-sticker-color", color);
  el.style.setProperty("--ccs-sticker-color2", color2);
  el.style.setProperty("--ccs-sticker-rotate", rotateDeg + "deg");
  el.style.setProperty("--ccs-sticker-scale", String(scale));
  el.style.setProperty("--ccs-sticker-opacity", String(opacity));
  el.style.setProperty("--ccs-sticker-flip-x", String(flipX ? -1 : 1));
  el.style.setProperty("--ccs-sticker-flip-y", String(flipY ? -1 : 1));

  const bob = prop(item, "bob", false) === true;
  const spin = prop(item, "spin", false) === true;
  const pulse = prop(item, "pulse", false) === true;
  const bobAmp = Math.max(0, Number(prop(item, "bobAmplitude", 8)) || 8);
  const bobSpeed = Math.max(0.2, Number(prop(item, "bobSpeed", 2)) || 2);
  const spinSpeed = Math.max(0.2, Number(prop(item, "spinSpeed", 4)) || 4);
  const pulseSpeed = Math.max(0.2, Number(prop(item, "pulseSpeed", 1.5)) || 1.5);

  el.classList.toggle("ccs-sticker-bob", bob);
  el.classList.toggle("ccs-sticker-spin", spin);
  el.classList.toggle("ccs-sticker-pulse", pulse);
  el.style.setProperty("--ccs-sticker-bob-amp", bobAmp + "px");
  el.style.setProperty("--ccs-sticker-bob-speed", bobSpeed + "s");
  el.style.setProperty("--ccs-sticker-spin-speed", spinSpeed + "s");
  el.style.setProperty("--ccs-sticker-pulse-speed", pulseSpeed + "s");

  renderPresetGraphic(el, preset, src, fit);
}

export function createStickerEl(item: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className = "ccs-shape ccs-sticker ccs-sticker-v-flat";
  applySticker(el, item);
  return el;
}

export function updateSticker(el: HTMLElement, item: LayoutItem): void {
  applySticker(el, item);
}
