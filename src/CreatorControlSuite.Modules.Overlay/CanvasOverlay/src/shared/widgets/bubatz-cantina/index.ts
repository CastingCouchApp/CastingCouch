import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { applySizeClass, applyVariantClasses, pickVariant } from "../../utils/look";
import "./bubatz-cantina.css";

export const BUBATZ_CANTINA_MODES = ["sign", "menu", "status", "ticker"] as const;
export type BubatzCantinaMode = (typeof BUBATZ_CANTINA_MODES)[number];

export const BUBATZ_CANTINA_VARIANTS = [
  "cantina-neon",
  "turquoise-glow",
  "holo-booth",
  "menu-board",
  "spacerun",
  "leaf-badge",
  "glass-booth",
  "pixel-cantina",
  "soft-haze",
  "outline-neon",
  "booth-card",
  "ticker-strip",
  "hyperspace",
  "blue-milk"
] as const;

export type BubatzCantinaVariant = (typeof BUBATZ_CANTINA_VARIANTS)[number];

export const BUBATZ_CANTINA_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  mini: { w: 280, h: 120, label: "Mini" },
  compact: { w: 360, h: 160, label: "Compact" },
  standard: { w: 480, h: 220, label: "Standard" },
  wide: { w: 720, h: 200, label: "Wide" },
  banner: { w: 960, h: 140, label: "Banner" },
  poster: { w: 420, h: 560, label: "Poster" }
};

const SIZE_KEYS = Object.keys(BUBATZ_CANTINA_SIZE_PRESETS);

const DEFAULT_MENU =
  "Blue Milk · 4 Credits\nBantha Burger · 8 Credits\nHyperspace Haze · 12 Credits\nCantina Special · 15 Credits";

function hexToRgba(hex: string, alpha: number): string {
  const raw = hex.replace("#", "").trim();
  if (raw.length !== 6) return hex;
  const r = Number.parseInt(raw.slice(0, 2), 16);
  const g = Number.parseInt(raw.slice(2, 4), 16);
  const b = Number.parseInt(raw.slice(4, 6), 16);
  if ([r, g, b].some((n) => Number.isNaN(n))) return hex;
  return `rgba(${r},${g},${b},${Math.max(0, Math.min(1, alpha))})`;
}

function resolveMode(item: LayoutItem): BubatzCantinaMode {
  const raw = String(prop(item, "mode", "sign") || "sign").toLowerCase();
  return (BUBATZ_CANTINA_MODES as readonly string[]).includes(raw)
    ? (raw as BubatzCantinaMode)
    : "sign";
}

function parseMenuLines(raw: unknown): string[] {
  return String(raw || "")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function buildTickerText(
  title: string,
  message: string,
  menuLines: string[],
  statusLabel: string,
  statusValue: string
): string {
  const status =
    statusLabel || statusValue
      ? [statusLabel, statusValue].filter(Boolean).join(": ")
      : "";
  return [title, message, ...menuLines, status].filter(Boolean).join("  ·  ");
}

/** Measure and start/stop marquee. Duplicated segments force overflow in ticker mode. */
export function syncMarquee(el: HTMLElement, item: LayoutItem, scroll: boolean): void {
  const track = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-track");
  if (!track) return;
  const inner = track.querySelector<HTMLElement>(".ccs-bubatz-cantina-marquee");
  if (!inner) return;

  if (!scroll) {
    track.classList.remove("is-scrolling");
    track.style.removeProperty("--ccs-bubatz-marquee-distance");
    track.style.removeProperty("--ccs-bubatz-marquee-duration");
    return;
  }

  void track.offsetWidth;
  const first = inner.querySelector<HTMLElement>(".ccs-bubatz-cantina-marquee-seg");
  const gap = Math.max(24, Number(prop(item, "repeatGap", 48)) || 48);
  const segmentWidth = first ? first.offsetWidth : inner.scrollWidth;
  const distance = Math.max(segmentWidth + gap, inner.scrollWidth - track.clientWidth);
  const scrolling = distance > 4;
  track.classList.toggle("is-scrolling", scrolling);
  if (scrolling) {
    track.style.setProperty("--ccs-bubatz-marquee-distance", distance + "px");
    const speed = Math.max(10, Number(prop(item, "speed", 40)) || 40);
    const duration = Math.max(4, Math.min(60, distance / speed));
    track.style.setProperty("--ccs-bubatz-marquee-duration", duration + "s");
  }
}

function applyAppearance(el: HTMLElement, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "cantina-neon"), BUBATZ_CANTINA_VARIANTS, "cantina-neon");
  const sizeKey = pickVariant(prop(item, "sizePreset", "standard"), SIZE_KEYS, "standard");
  applyVariantClasses(el, "ccs-bubatz-cantina-v-", variant, BUBATZ_CANTINA_VARIANTS);
  applySizeClass(el, "ccs-bubatz-cantina-s-", sizeKey, SIZE_KEYS);

  const mode = resolveMode(item);
  BUBATZ_CANTINA_MODES.forEach((name) => el.classList.remove("ccs-bubatz-cantina-mode-" + name));
  el.classList.add("ccs-bubatz-cantina-mode-" + mode);
  el.dataset.mode = mode;

  const color = String(prop(item, "color", "#5CDB6A") || "#5CDB6A");
  const color2 = String(prop(item, "color2", "#2EE6C5") || "#2EE6C5");
  const color3 = String(prop(item, "color3", "#E8B84A") || "#E8B84A");
  const textColor = String(prop(item, "textColor", "#EAF6FF") || "#EAF6FF");
  const mutedColor = String(prop(item, "mutedColor", "#9EC4D8") || "#9EC4D8");
  const bgColor = String(prop(item, "bgColor", "#060B14") || "#060B14");
  const bgOpacity = Number(prop(item, "bgOpacity", 0.88));
  const opacity = Number.isFinite(bgOpacity) ? Math.max(0, Math.min(1, bgOpacity)) : 0.88;

  el.style.setProperty("--ccs-bubatz-accent", color);
  el.style.setProperty("--ccs-bubatz-turquoise", color2);
  el.style.setProperty("--ccs-bubatz-gold", color3);
  el.style.setProperty("--ccs-bubatz-text", textColor);
  el.style.setProperty("--ccs-bubatz-muted", mutedColor);
  el.style.setProperty("--ccs-bubatz-bg", hexToRgba(bgColor, opacity));

  const titleFont = String(prop(item, "titleFontFamily", "") || prop(item, "fontFamily", "") || "");
  const bodyFont = String(prop(item, "bodyFontFamily", "") || prop(item, "fontFamily", "") || "");
  if (titleFont) el.style.setProperty("--ccs-bubatz-title-font", titleFont);
  else el.style.removeProperty("--ccs-bubatz-title-font");
  if (bodyFont) el.style.setProperty("--ccs-bubatz-body-font", bodyFont);
  else el.style.removeProperty("--ccs-bubatz-body-font");

  const titleSize = Number(prop(item, "titleSizePx", 0)) || 0;
  const bodySize = Number(prop(item, "bodySizePx", 0)) || 0;
  const radius = Number(prop(item, "borderRadiusPx", 16));
  const padding = Number(prop(item, "paddingPx", 20));
  const gap = Number(prop(item, "gapPx", 10));

  if (titleSize) el.style.setProperty("--ccs-bubatz-title-size", titleSize + "px");
  else el.style.removeProperty("--ccs-bubatz-title-size");
  if (bodySize) el.style.setProperty("--ccs-bubatz-body-size", bodySize + "px");
  else el.style.removeProperty("--ccs-bubatz-body-size");
  el.style.setProperty("--ccs-bubatz-radius", (Number.isFinite(radius) ? radius : 16) + "px");
  el.style.setProperty("--ccs-bubatz-pad", (Number.isFinite(padding) ? padding : 20) + "px");
  el.style.setProperty("--ccs-bubatz-gap", (Number.isFinite(gap) ? gap : 10) + "px");

  const scroll = mode === "ticker" || prop(item, "scroll", false) === true;
  el.classList.toggle("ccs-bubatz-uppercase", prop(item, "uppercase", false) === true);
  el.classList.toggle("ccs-bubatz-pulse-leaf", prop(item, "pulseLeaf", true) !== false);
  el.classList.toggle("ccs-bubatz-twinkle", prop(item, "twinkleStars", true) !== false);
  el.classList.toggle("ccs-bubatz-scroll", scroll);
}

export function createBubatzCantinaEl(item?: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className =
    "ccs-bubatz-cantina ccs-bubatz-cantina-v-cantina-neon ccs-bubatz-cantina-s-standard ccs-bubatz-cantina-mode-sign";
  el.innerHTML =
    `<div class="ccs-bubatz-cantina-stars" aria-hidden="true">` +
    `<span></span><span></span><span></span><span></span><span></span><span></span>` +
    `</div>` +
    `<div class="ccs-bubatz-cantina-chrome">` +
    `<span class="ccs-bubatz-cantina-leaf" aria-hidden="true">🌿</span>` +
    `<div class="ccs-bubatz-cantina-body">` +
    `<div class="ccs-bubatz-cantina-title"></div>` +
    `<div class="ccs-bubatz-cantina-subtitle"></div>` +
    `<div class="ccs-bubatz-cantina-message"></div>` +
    `<ul class="ccs-bubatz-cantina-menu"></ul>` +
    `<div class="ccs-bubatz-cantina-status">` +
    `<span class="ccs-bubatz-cantina-status-label"></span>` +
    `<span class="ccs-bubatz-cantina-status-value"></span>` +
    `</div>` +
    `<div class="ccs-bubatz-cantina-track">` +
    `<div class="ccs-bubatz-cantina-marquee"></div>` +
    `</div>` +
    `</div>` +
    `</div>`;
  if (item) updateBubatzCantina(el, item);
  return el;
}

export function updateBubatzCantina(el: HTMLElement, item: LayoutItem): void {
  applyAppearance(el, item);

  const mode = resolveMode(item);
  const title = String(prop(item, "title", "biomilchs Bubatz Cantina") || "");
  const subtitle = String(prop(item, "subtitle", "Open late · Orbit Sector 7") || "");
  const message = String(prop(item, "message", "Blue Milk & Hyperspace Haze — heute happy hour") || "");
  const statusLabel = String(prop(item, "statusLabel", "Special") || "");
  const statusValue = String(prop(item, "statusValue", "Bubatz live") || "");
  const menuLines = parseMenuLines(prop(item, "menuLines", DEFAULT_MENU));
  const icon = String(prop(item, "icon", "🌿") || "🌿");

  const showLeaf = prop(item, "showLeaf", true) !== false;
  const showStars = prop(item, "showStars", true) !== false;
  const showTitle = prop(item, "showTitle", true) !== false;
  const showSubtitle = prop(item, "showSubtitle", true) !== false;
  const showMessage = prop(item, "showMessage", true) !== false;
  const showMenu = prop(item, "showMenu", true) !== false;
  const showStatus = prop(item, "showStatus", true) !== false;

  const leaf = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-leaf");
  const stars = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-stars");
  const titleEl = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-title");
  const subtitleEl = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-subtitle");
  const messageEl = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-message");
  const menuEl = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-menu");
  const statusEl = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-status");
  const statusLabelEl = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-status-label");
  const statusValueEl = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-status-value");
  const track = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-track");
  const marquee = el.querySelector<HTMLElement>(".ccs-bubatz-cantina-marquee");

  if (leaf) {
    leaf.style.display = showLeaf ? "" : "none";
    leaf.textContent = icon;
  }
  if (stars) stars.style.display = showStars ? "" : "none";

  const isTicker = mode === "ticker";

  if (titleEl) {
    titleEl.style.display = !isTicker && showTitle && title ? "" : "none";
    titleEl.textContent = title;
  }
  if (subtitleEl) {
    subtitleEl.style.display = !isTicker && showSubtitle && subtitle ? "" : "none";
    subtitleEl.textContent = subtitle;
  }
  if (messageEl) {
    const visible = !isTicker && showMessage && message && (mode === "sign" || mode === "status");
    messageEl.style.display = visible ? "" : "none";
    messageEl.textContent = message;
  }
  if (menuEl) {
    const visible = !isTicker && showMenu && menuLines.length > 0 && (mode === "menu" || mode === "sign");
    menuEl.style.display = visible ? "" : "none";
    menuEl.innerHTML = menuLines.map((line) => `<li>${escapeHtml(line)}</li>`).join("");
  }
  if (statusEl && statusLabelEl && statusValueEl) {
    const visible =
      !isTicker && showStatus && (statusLabel || statusValue) && (mode === "status" || mode === "sign");
    statusEl.style.display = visible ? "" : "none";
    statusLabelEl.textContent = statusLabel;
    statusValueEl.textContent = statusValue;
  }

  const tickerText = buildTickerText(title, message, menuLines, statusLabel, statusValue);
  const scroll = isTicker || prop(item, "scroll", false) === true;
  const showTrack = isTicker || scroll;

  if (track && marquee) {
    track.classList.toggle("is-active", showTrack && Boolean(tickerText));
    if (showTrack && tickerText) {
      // Two segments → continuous loop even when text is shorter than the track.
      const seg = `<span class="ccs-bubatz-cantina-marquee-seg">${escapeHtml(tickerText)}</span>`;
      marquee.innerHTML = seg + seg;
      const gap = Math.max(24, Number(prop(item, "repeatGap", 48)) || 48);
      marquee.style.setProperty("--ccs-bubatz-repeat-gap", gap + "px");
    } else {
      marquee.innerHTML = "";
      track.classList.remove("is-scrolling");
    }
    requestAnimationFrame(() => syncMarquee(el, item, showTrack && Boolean(tickerText)));
  }

  const empty =
    !title &&
    !subtitle &&
    !message &&
    menuLines.length === 0 &&
    !statusLabel &&
    !statusValue;
  el.classList.toggle("is-empty", empty);
  el.classList.toggle("is-hidden", empty && prop(item, "hideWhenEmpty", false) === true);
}
