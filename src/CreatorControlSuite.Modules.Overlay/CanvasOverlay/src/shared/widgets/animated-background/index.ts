import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { pickVariant, applyVariantClasses, applySizeClass } from "../../utils/look";
import {
  isParallaxBackgroundVariant,
  syncParallaxBackground,
  teardownParallaxBackground
} from "./parallax";
import {
  isMatrixBackgroundVariant,
  syncMatrixBackground,
  teardownMatrixBackground
} from "./matrix-rain";
import "./animated-background.css";

export const ANIMATED_BACKGROUND_VARIANTS = [
  "hacker",
  "cyber",
  "retro",
  "vaporwave",
  "meme",
  "queer",
  "peace",
  "street",
  "aurora",
  "ocean",
  "fire",
  "cosmic",
  "glitch",
  "pixel",
  "lava",
  "ice",
  "disco",
  "rain",
  "bubbles",
  "grid",
  "sunset",
  "forest",
  "candy",
  "noir",
  "mountains",
  "alpine",
  "fuji",
  "mesa",
  "neon-peaks",
  "mist-peaks",
  "lowpoly",
  "papercut",
  "floating",
  "ridge-storm"
] as const;

export type AnimatedBackgroundVariant = (typeof ANIMATED_BACKGROUND_VARIANTS)[number];

export const ANIMATED_BACKGROUND_VARIANT_LABELS: Record<AnimatedBackgroundVariant, string> = {
  hacker: "Matrix Rain",
  cyber: "Cyber Neon",
  retro: "Retro CRT",
  vaporwave: "Vaporwave",
  meme: "Meme Chaos",
  queer: "Queer Pride",
  peace: "Peace Soft",
  street: "Street Graffiti",
  aurora: "Aurora",
  ocean: "Ocean",
  fire: "Fire Ember",
  cosmic: "Cosmic Stars",
  glitch: "Glitch",
  pixel: "Pixel 8-Bit",
  lava: "Lava",
  ice: "Ice Crystal",
  disco: "Disco",
  rain: "Rain",
  bubbles: "Bubbles",
  grid: "HUD Grid",
  sunset: "Sunset",
  forest: "Forest",
  candy: "Candy Pop",
  noir: "Noir Film",
  mountains: "Parallax Mountains",
  alpine: "Alpine Snow",
  fuji: "Fuji Sunrise",
  mesa: "Desert Mesa",
  "neon-peaks": "Neon Peaks",
  "mist-peaks": "Mist Peaks",
  lowpoly: "Lowpoly Peaks",
  papercut: "Papercut Hills",
  floating: "Floating Isles",
  "ridge-storm": "Ridge Storm"
};

export const ANIMATED_BACKGROUND_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  fullscreen: { w: 1920, h: 1080, label: "Fullscreen 1080p" },
  hd: { w: 1280, h: 720, label: "HD 720p" },
  square: { w: 900, h: 900, label: "Square" },
  banner: { w: 1920, h: 360, label: "Banner" },
  vertical: { w: 1080, h: 1920, label: "Vertical" }
};

const SIZE_KEYS = Object.keys(ANIMATED_BACKGROUND_SIZE_PRESETS);

const VARIANT_DEFAULTS: Record<AnimatedBackgroundVariant, { color: string; color2: string; color3: string }> = {
  hacker: { color: "#00ff66", color2: "#003311", color3: "#99ffbb" },
  cyber: { color: "#00f0ff", color2: "#ff00aa", color3: "#7a00ff" },
  retro: { color: "#ff6ec7", color2: "#00e5ff", color3: "#ffe566" },
  vaporwave: { color: "#ff71ce", color2: "#01cdfe", color3: "#b967ff" },
  meme: { color: "#ff0000", color2: "#ffff00", color3: "#00ffff" },
  queer: { color: "#e40303", color2: "#ff8c00", color3: "#008026" },
  peace: { color: "#7ec8a3", color2: "#f4e2c8", color3: "#5b8c5a" },
  street: { color: "#ff3d00", color2: "#ffeb3b", color3: "#00e5ff" },
  aurora: { color: "#00e5c0", color2: "#5cf0ff", color3: "#a855f7" },
  ocean: { color: "#0ea5e9", color2: "#0369a1", color3: "#67e8f9" },
  fire: { color: "#ff4500", color2: "#ffb347", color3: "#ff0040" },
  cosmic: { color: "#a78bfa", color2: "#38bdf8", color3: "#f0abfc" },
  glitch: { color: "#ff0040", color2: "#00ffe5", color3: "#ffffff" },
  pixel: { color: "#7cfc00", color2: "#ff1493", color3: "#1e90ff" },
  lava: { color: "#ff2d00", color2: "#ff9a00", color3: "#3a0a00" },
  ice: { color: "#a5f3fc", color2: "#38bdf8", color3: "#e0f2fe" },
  disco: { color: "#ff00aa", color2: "#00ffcc", color3: "#ffe600" },
  rain: { color: "#94a3b8", color2: "#38bdf8", color3: "#0f172a" },
  bubbles: { color: "#67e8f9", color2: "#f9a8d4", color3: "#c4b5fd" },
  grid: { color: "#22d3ee", color2: "#0ea5e9", color3: "#082f49" },
  sunset: { color: "#fb923c", color2: "#f472b6", color3: "#7c3aed" },
  forest: { color: "#4ade80", color2: "#166534", color3: "#a3e635" },
  candy: { color: "#fb7185", color2: "#a78bfa", color3: "#67e8f9" },
  noir: { color: "#e5e5e5", color2: "#737373", color3: "#171717" },
  mountains: { color: "#ff8c42", color2: "#7c3aed", color3: "#1e1b4b" },
  alpine: { color: "#dbeafe", color2: "#93c5fd", color3: "#1e3a5f" },
  fuji: { color: "#ff6b4a", color2: "#ffd1a1", color3: "#2a1b3d" },
  mesa: { color: "#f59e0b", color2: "#ea580c", color3: "#7c2d12" },
  "neon-peaks": { color: "#00f0ff", color2: "#ff00aa", color3: "#12061f" },
  "mist-peaks": { color: "#cbd5e1", color2: "#94a3b8", color3: "#334155" },
  lowpoly: { color: "#34d399", color2: "#0ea5e9", color3: "#1e293b" },
  papercut: { color: "#fef3c7", color2: "#f9a8d4", color3: "#7c3aed" },
  floating: { color: "#a78bfa", color2: "#67e8f9", color3: "#1e1b4b" },
  "ridge-storm": { color: "#94a3b8", color2: "#64748b", color3: "#0f172a" }
};

function clamp(n: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, n));
}

export function applyAnimatedBackground(el: HTMLElement, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "cyber"), ANIMATED_BACKGROUND_VARIANTS, "cyber") as AnimatedBackgroundVariant;
  applyVariantClasses(el, "ccs-animated-bg-v-", variant, ANIMATED_BACKGROUND_VARIANTS);

  const sizeKey = pickVariant(prop(item, "sizePreset", "fullscreen"), SIZE_KEYS, "fullscreen");
  applySizeClass(el, "ccs-animated-bg-s-", sizeKey, SIZE_KEYS);

  const defaults = VARIANT_DEFAULTS[variant];
  const color = String(prop(item, "color", defaults.color) || defaults.color);
  const color2 = String(prop(item, "color2", defaults.color2) || defaults.color2);
  const color3 = String(prop(item, "color3", defaults.color3) || defaults.color3);
  const speed = clamp(Number(prop(item, "speed", 1)) || 1, 0.1, 5);
  const intensity = clamp(Number(prop(item, "intensity", 0.85)) || 0.85, 0, 1);
  const opacity = clamp(Number(prop(item, "opacity", 1)) || 1, 0, 1);
  const density = clamp(Number(prop(item, "density", 1)) || 1, 0.1, 5);
  const radius = Math.max(0, Number(prop(item, "borderRadiusPx", 0)) || 0);
  const paused = prop(item, "paused", false) === true;
  const vignette = prop(item, "vignette", true) !== false;

  el.style.setProperty("--ccs-abg-c1", color);
  el.style.setProperty("--ccs-abg-c2", color2);
  el.style.setProperty("--ccs-abg-c3", color3);
  el.style.setProperty("--ccs-abg-speed", String(speed));
  el.style.setProperty("--ccs-abg-intensity", String(intensity));
  el.style.setProperty("--ccs-abg-opacity", String(opacity));
  el.style.setProperty("--ccs-abg-density", String(density));
  el.style.setProperty("--ccs-abg-radius", radius + "px");
  el.style.opacity = String(opacity);
  el.dataset.paused = paused ? "1" : "0";
  el.dataset.vignette = vignette ? "1" : "0";
  el.classList.toggle("is-paused", paused);
  el.classList.toggle("has-vignette", vignette);

  if (isParallaxBackgroundVariant(variant)) {
    teardownMatrixBackground(el);
    syncParallaxBackground(el, item, variant, {
      color,
      color2,
      color3,
      speed,
      intensity,
      density,
      paused
    });
  } else if (isMatrixBackgroundVariant(variant)) {
    teardownParallaxBackground(el);
    syncMatrixBackground(el, item, variant, {
      color,
      color2,
      color3,
      speed,
      intensity,
      density,
      paused
    });
  } else {
    teardownParallaxBackground(el);
    teardownMatrixBackground(el);
  }
}

export function createAnimatedBackgroundEl(item?: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className = "ccs-animated-bg ccs-animated-bg-v-cyber";
  el.setAttribute("aria-hidden", "true");
  el.innerHTML =
    `<div class="ccs-abg-base"></div>` +
    `<div class="ccs-abg-layer ccs-abg-a"></div>` +
    `<div class="ccs-abg-layer ccs-abg-b"></div>` +
    `<div class="ccs-abg-layer ccs-abg-fx"></div>` +
    `<div class="ccs-abg-parallax" hidden>` +
    `<div class="ccs-abg-sky"></div>` +
    `<div class="ccs-abg-decor"></div>` +
    `<div class="ccs-abg-ridges"></div>` +
    `<canvas class="ccs-abg-particles"></canvas>` +
    `</div>` +
    `<canvas class="ccs-abg-matrix" hidden></canvas>`;
  if (item) updateAnimatedBackground(el, item);
  return el;
}

export function updateAnimatedBackground(el: HTMLElement, item: LayoutItem): void {
  applyAnimatedBackground(el, item);
}
