import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { applySizeClass, applyVariantClasses, pickVariant } from "../../utils/look";
import { encodeQrSvg, type QrErrorCorrection } from "./qr-encode";
import "./qr-code.css";

export const QR_CODE_VARIANTS = [
  "classic",
  "neon",
  "glass",
  "minimal",
  "bold",
  "outline",
  "framed",
  "inverted"
] as const;

export const QR_CODE_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  sm: { w: 160, h: 200, label: "Small" },
  md: { w: 220, h: 260, label: "Medium" },
  lg: { w: 300, h: 340, label: "Large" },
  xl: { w: 400, h: 440, label: "XL" }
};

const SIZE_KEYS = Object.keys(QR_CODE_SIZE_PRESETS);

type QrCodeEl = HTMLElement & {
  _lastUrl?: string;
};

function normalizeEc(raw: unknown): QrErrorCorrection {
  const value = String(raw || "M").toUpperCase();
  return (["L", "M", "Q", "H"] as const).includes(value as QrErrorCorrection)
    ? (value as QrErrorCorrection)
    : "M";
}

function applyAppearance(el: QrCodeEl, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "classic"), QR_CODE_VARIANTS);
  const sizeKey = pickVariant(prop(item, "sizePreset", "md"), SIZE_KEYS, "md");
  applyVariantClasses(el, "ccs-qr-code-v-", variant, QR_CODE_VARIANTS);
  applySizeClass(el, "ccs-qr-code-s-", sizeKey, SIZE_KEYS);

  const color = String(prop(item, "color", "") || "");
  const fg = String(prop(item, "fg", "") || "");
  const bg = String(prop(item, "bg", "") || "");
  const fontFamily = String(prop(item, "fontFamily", "") || "");
  const borderRadiusPx = Number(prop(item, "borderRadiusPx", 0)) || 0;
  const paddingPx = Number(prop(item, "paddingPx", 0)) || 0;
  const frame = String(prop(item, "frame", "none") || "none");
  const invert = prop(item, "invert", false) === true;

  if (color) el.style.setProperty("--ccs-qr-accent", color);
  else el.style.removeProperty("--ccs-qr-accent");
  if (fg) el.style.setProperty("--ccs-qr-fg", fg);
  else el.style.removeProperty("--ccs-qr-fg");
  if (bg) el.style.setProperty("--ccs-qr-bg", bg);
  else el.style.removeProperty("--ccs-qr-bg");
  if (fontFamily) el.style.setProperty("--ccs-qr-font", fontFamily);
  else el.style.removeProperty("--ccs-qr-font");
  if (borderRadiusPx || borderRadiusPx === 0) el.style.setProperty("--ccs-qr-radius", borderRadiusPx + "px");
  else el.style.removeProperty("--ccs-qr-radius");
  if (paddingPx) el.style.setProperty("--ccs-qr-pad", paddingPx + "px");
  else el.style.removeProperty("--ccs-qr-pad");

  el.classList.toggle("ccs-qr-invert", invert);
  el.classList.toggle("ccs-qr-frame", frame !== "none" && frame !== "");
  el.classList.toggle("ccs-qr-frame-solid", frame === "solid");
  el.classList.toggle("ccs-qr-frame-accent", frame === "accent");
}

function renderQr(el: QrCodeEl, item: LayoutItem): void {
  const url = String(prop(item, "url", "") || "").trim();
  const hideWhenEmptyUrl = prop(item, "hideWhenEmptyUrl", true) !== false;
  const showCaption = prop(item, "showCaption", true) !== false;
  const caption = String(prop(item, "caption", "") || "");
  const captionPosition = String(prop(item, "captionPosition", "bottom") || "bottom");
  const showLogo = prop(item, "showLogo", false) === true;
  const logoUrl = String(prop(item, "logoUrl", "") || "");
  const quietZone = Math.max(0, Number(prop(item, "quietZone", 2)) || 2);
  const ec = normalizeEc(prop(item, "errorCorrection", "M"));

  const matrixWrap = el.querySelector<HTMLElement>(".ccs-qr-code-matrix");
  const placeholder = el.querySelector<HTMLElement>(".ccs-qr-code-placeholder");
  const captionEl = el.querySelector<HTMLElement>(".ccs-qr-code-caption");
  const logo = el.querySelector<HTMLImageElement>(".ccs-qr-code-logo");

  el.classList.toggle("is-empty", !url);
  el.classList.toggle("is-hidden", hideWhenEmptyUrl && !url);
  el.classList.remove("ccs-qr-caption-top", "ccs-qr-caption-bottom");
  el.classList.add(captionPosition === "top" ? "ccs-qr-caption-top" : "ccs-qr-caption-bottom");

  if (captionEl) {
    captionEl.style.display = showCaption && caption ? "" : "none";
    captionEl.textContent = caption;
  }
  if (logo) {
    logo.style.display = showLogo && logoUrl ? "" : "none";
    if (logoUrl) logo.src = logoUrl;
    else logo.removeAttribute("src");
  }

  if (!url) {
    if (matrixWrap) matrixWrap.innerHTML = "";
    if (placeholder) placeholder.style.display = "";
    return;
  }
  if (placeholder) placeholder.style.display = "none";

  const fg = String(prop(item, "fg", "#111111") || "#111111");
  const bg = String(prop(item, "bg", "#ffffff") || "#ffffff");
  const invert = prop(item, "invert", false) === true;
  const fgColor = invert ? bg : fg;
  const bgColor = invert ? fg : bg;
  const cacheKey = [url, fgColor, bgColor, quietZone, ec].join("|");

  if (el._lastUrl !== cacheKey) {
    try {
      const svg = encodeQrSvg(url, { fg: fgColor, bg: bgColor, quietZone, errorCorrection: ec });
      if (matrixWrap) matrixWrap.innerHTML = svg;
      el._lastUrl = cacheKey;
    } catch {
      if (matrixWrap) matrixWrap.innerHTML = "";
      if (placeholder) {
        placeholder.style.display = "";
        placeholder.textContent = "QR zu lang";
      }
    }
  }
}

export function createQrCodeEl(item?: LayoutItem): QrCodeEl {
  const el = document.createElement("div") as QrCodeEl;
  el.className = "ccs-qr-code ccs-qr-code-v-classic ccs-qr-code-s-md";
  el.innerHTML =
    `<div class="ccs-qr-code-caption"></div>` +
    `<div class="ccs-qr-code-body">` +
    `<div class="ccs-qr-code-matrix-wrap">` +
    `<div class="ccs-qr-code-matrix"></div>` +
    `<img class="ccs-qr-code-logo" alt="" draggable="false" />` +
    `<div class="ccs-qr-code-placeholder">URL eingeben</div>` +
    `</div>` +
    `</div>`;
  if (item) updateQrCode(el, item);
  return el;
}

export function updateQrCode(el: QrCodeEl, item: LayoutItem): void {
  applyAppearance(el, item);
  renderQr(el, item);
}
