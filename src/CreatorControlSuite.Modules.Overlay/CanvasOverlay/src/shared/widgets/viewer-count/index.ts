import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { applySizeClass, applyVariantClasses, pickVariant } from "../../utils/look";
import "./viewer-count.css";

export const VIEWER_COUNT_VARIANTS = [
  "classic",
  "neon",
  "glass",
  "cyber",
  "minimal",
  "bold",
  "soft",
  "outline",
  "hud",
  "pixel",
  "stripe",
  "capsule"
] as const;

export const VIEWER_COUNT_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  mini: { w: 120, h: 48, label: "Mini" },
  compact: { w: 180, h: 64, label: "Compact" },
  standard: { w: 240, h: 80, label: "Standard" },
  wide: { w: 320, h: 88, label: "Wide" },
  pill: { w: 200, h: 56, label: "Pill" }
};

const SIZE_KEYS = Object.keys(VIEWER_COUNT_SIZE_PRESETS);

type ViewerCountEl = HTMLElement & {
  _lastCount?: number;
};

function formatCount(value: number, mode: string): string {
  const n = Math.max(0, Math.floor(Number(value) || 0));
  if (mode !== "compact") return String(n);
  if (n < 1000) return String(n);
  if (n < 1_000_000) {
    const k = n / 1000;
    return (k >= 10 ? Math.round(k) : Math.round(k * 10) / 10) + "K";
  }
  const m = n / 1_000_000;
  return (m >= 10 ? Math.round(m) : Math.round(m * 10) / 10) + "M";
}

function isStreamLive(data: Record<string, unknown> | null | undefined): boolean {
  const stream = ((data && data.stream) || {}) as Record<string, unknown>;
  if (stream.isLive === true) return true;
  if (stream.isLive === false) return false;
  return Number(stream.viewerCount || 0) > 0;
}

function applyAppearance(el: ViewerCountEl, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "classic"), VIEWER_COUNT_VARIANTS);
  const sizeKey = pickVariant(prop(item, "sizePreset", "standard"), SIZE_KEYS, "standard");
  applyVariantClasses(el, "ccs-viewer-count-v-", variant, VIEWER_COUNT_VARIANTS);
  applySizeClass(el, "ccs-viewer-count-s-", sizeKey, SIZE_KEYS);

  const color = String(prop(item, "color", "") || "");
  const bg = String(prop(item, "backgroundColor", "") || "");
  const fontFamily = String(prop(item, "fontFamily", "") || "");
  const fontSizePx = Number(prop(item, "fontSizePx", 0)) || 0;
  if (color) el.style.setProperty("--ccs-vc-color", color);
  else el.style.removeProperty("--ccs-vc-color");
  if (bg) el.style.setProperty("--ccs-vc-bg", bg);
  else el.style.removeProperty("--ccs-vc-bg");
  if (fontFamily) el.style.setProperty("--ccs-vc-font", fontFamily);
  else el.style.removeProperty("--ccs-vc-font");
  if (fontSizePx) el.style.setProperty("--ccs-vc-font-size", fontSizePx + "px");
  else el.style.removeProperty("--ccs-vc-font-size");

  el.classList.toggle("ccs-viewer-pulse", prop(item, "pulseOnChange", false) === true);
}

export function createViewerCountEl(item?: LayoutItem): ViewerCountEl {
  const el = document.createElement("div") as ViewerCountEl;
  el.className = "ccs-viewer-count ccs-viewer-count-v-classic ccs-viewer-count-s-standard";
  el.innerHTML =
    `<div class="ccs-viewer-count-icon" aria-hidden="true">👁</div>` +
    `<div class="ccs-viewer-count-body">` +
    `<div class="ccs-viewer-count-label"></div>` +
    `<div class="ccs-viewer-count-row">` +
    `<span class="ccs-viewer-count-value"></span>` +
    `<span class="ccs-viewer-count-delta"></span>` +
    `</div>` +
    `<div class="ccs-viewer-count-peak"></div>` +
    `</div>`;
  if (item) updateViewerCount(el, item);
  return el;
}

export function updateViewerCount(
  el: ViewerCountEl,
  item: LayoutItem,
  data?: Record<string, unknown> | null
): void {
  applyAppearance(el, item);
  const stream = ((data && data.stream) || {}) as Record<string, unknown>;
  const stats = ((data && data.stats) || {}) as Record<string, unknown>;
  const count = Number(stream.viewerCount || 0);
  const peak = Number(stats.peakViewers || 0);
  const format = String(prop(item, "format", "plain") || "plain");
  const showLabel = prop(item, "showLabel", true) !== false;
  const showIcon = prop(item, "showIcon", true) !== false;
  const showPeak = prop(item, "showPeak", false) === true;
  const showDelta = prop(item, "showDelta", false) === true;
  const hideWhenOffline = prop(item, "hideWhenOffline", false) === true;
  const label = String(prop(item, "label", "Viewers") || "Viewers");
  const live = isStreamLive(data);

  el.classList.toggle("is-offline", !live);
  el.classList.toggle("is-hidden", hideWhenOffline && !live);

  const icon = el.querySelector<HTMLElement>(".ccs-viewer-count-icon");
  const labelEl = el.querySelector<HTMLElement>(".ccs-viewer-count-label");
  const valueEl = el.querySelector<HTMLElement>(".ccs-viewer-count-value");
  const deltaEl = el.querySelector<HTMLElement>(".ccs-viewer-count-delta");
  const peakEl = el.querySelector<HTMLElement>(".ccs-viewer-count-peak");

  if (icon) icon.style.display = showIcon ? "" : "none";
  if (labelEl) {
    labelEl.style.display = showLabel ? "" : "none";
    labelEl.textContent = label;
  }
  if (valueEl) valueEl.textContent = formatCount(count, format);
  if (peakEl) {
    peakEl.style.display = showPeak ? "" : "none";
    peakEl.textContent = showPeak ? `Peak ${formatCount(peak, format)}` : "";
  }
  if (deltaEl) {
    deltaEl.style.display = showDelta ? "" : "none";
    const prev = el._lastCount;
    if (showDelta && prev != null && prev !== count) {
      const diff = count - prev;
      deltaEl.textContent = diff > 0 ? `+${diff}` : String(diff);
      deltaEl.classList.toggle("is-up", diff > 0);
      deltaEl.classList.toggle("is-down", diff < 0);
    } else {
      deltaEl.textContent = "";
    }
  }

  if (el._lastCount != null && count !== el._lastCount && prop(item, "pulseOnChange", false) === true) {
    el.classList.add("ccs-viewer-bump");
    window.setTimeout(() => el.classList.remove("ccs-viewer-bump"), 400);
  }
  el._lastCount = count;
}
