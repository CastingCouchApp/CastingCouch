import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { applySizeClass, applyVariantClasses, pickVariant } from "../../utils/look";
import "./lower-third.css";

export const LOWER_THIRD_VARIANTS = [
  "classic",
  "neon",
  "glass",
  "cyber",
  "minimal",
  "bold",
  "soft",
  "outline",
  "hud",
  "broadcast",
  "esport",
  "ribbon",
  "split",
  "boxed",
  "underline"
] as const;

export const LOWER_THIRD_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  compact: { w: 420, h: 72, label: "Compact" },
  standard: { w: 560, h: 96, label: "Standard" },
  wide: { w: 720, h: 110, label: "Wide" },
  banner: { w: 900, h: 88, label: "Banner" },
  tall: { w: 560, h: 128, label: "Tall" }
};

const SIZE_KEYS = Object.keys(LOWER_THIRD_SIZE_PRESETS);

type LowerThirdEl = HTMLElement;

function applyAppearance(el: LowerThirdEl, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "classic"), LOWER_THIRD_VARIANTS);
  const sizeKey = pickVariant(prop(item, "sizePreset", "standard"), SIZE_KEYS, "standard");
  applyVariantClasses(el, "ccs-lower-third-v-", variant, LOWER_THIRD_VARIANTS);
  applySizeClass(el, "ccs-lower-third-s-", sizeKey, SIZE_KEYS);

  const layout = String(prop(item, "layout", "left") || "left");
  const avatarShape = String(prop(item, "avatarShape", "circle") || "circle");
  const accentPosition = String(prop(item, "accentPosition", "left") || "left");
  const entrance = String(prop(item, "entrance", "slide") || "slide");

  el.classList.remove("ccs-lower-third-layout-left", "ccs-lower-third-layout-center", "ccs-lower-third-layout-right");
  el.classList.add(
    "ccs-lower-third-layout-" + (["left", "center", "right"].includes(layout) ? layout : "left")
  );
  el.classList.remove("ccs-lower-third-avatar-circle", "ccs-lower-third-avatar-square", "ccs-lower-third-avatar-rounded");
  el.classList.add(
    "ccs-lower-third-avatar-" +
      (["circle", "square", "rounded"].includes(avatarShape) ? avatarShape : "circle")
  );
  el.classList.remove("ccs-lower-third-accent-left", "ccs-lower-third-accent-top", "ccs-lower-third-accent-bottom");
  el.classList.add(
    "ccs-lower-third-accent-" +
      (["left", "top", "bottom"].includes(accentPosition) ? accentPosition : "left")
  );
  el.classList.remove("ccs-lower-third-entrance-slide", "ccs-lower-third-entrance-fade", "ccs-lower-third-entrance-pop");
  el.classList.add(
    "ccs-lower-third-entrance-" + (["slide", "fade", "pop"].includes(entrance) ? entrance : "slide")
  );

  const color = String(prop(item, "color", "") || "");
  const color2 = String(prop(item, "color2", "") || "");
  const textColor = String(prop(item, "textColor", "") || "");
  const subtitleColor = String(prop(item, "subtitleColor", "") || "");
  const fontFamily = String(prop(item, "fontFamily", "") || "");
  const subtitleFont = String(prop(item, "subtitleFontFamily", "") || "");
  const borderRadiusPx = Number(prop(item, "borderRadiusPx", 0)) || 0;
  const paddingPx = Number(prop(item, "paddingPx", 0)) || 0;
  const gapPx = Number(prop(item, "gapPx", 0)) || 0;

  if (color) el.style.setProperty("--ccs-lt-accent", color);
  else el.style.removeProperty("--ccs-lt-accent");
  if (color2) el.style.setProperty("--ccs-lt-accent2", color2);
  else el.style.removeProperty("--ccs-lt-accent2");
  if (textColor) el.style.setProperty("--ccs-lt-text", textColor);
  else el.style.removeProperty("--ccs-lt-text");
  if (subtitleColor) el.style.setProperty("--ccs-lt-subtitle", subtitleColor);
  else el.style.removeProperty("--ccs-lt-subtitle");
  if (fontFamily) el.style.setProperty("--ccs-lt-font", fontFamily);
  else el.style.removeProperty("--ccs-lt-font");
  if (subtitleFont) el.style.setProperty("--ccs-lt-subtitle-font", subtitleFont);
  else el.style.removeProperty("--ccs-lt-subtitle-font");
  if (borderRadiusPx || borderRadiusPx === 0) el.style.setProperty("--ccs-lt-radius", borderRadiusPx + "px");
  else el.style.removeProperty("--ccs-lt-radius");
  if (paddingPx) el.style.setProperty("--ccs-lt-pad", paddingPx + "px");
  else el.style.removeProperty("--ccs-lt-pad");
  if (gapPx) el.style.setProperty("--ccs-lt-gap", gapPx + "px");
  else el.style.removeProperty("--ccs-lt-gap");
}

export function createLowerThirdEl(item?: LayoutItem): LowerThirdEl {
  const el = document.createElement("div") as LowerThirdEl;
  el.className =
    "ccs-lower-third ccs-lower-third-v-classic ccs-lower-third-s-standard ccs-lower-third-layout-left ccs-lower-third-avatar-circle ccs-lower-third-accent-left ccs-lower-third-entrance-slide";
  el.innerHTML =
    `<div class="ccs-lower-third-accent-bar"></div>` +
    `<div class="ccs-lower-third-inner">` +
    `<div class="ccs-lower-third-avatar-wrap">` +
    `<img class="ccs-lower-third-avatar" alt="" draggable="false" />` +
    `</div>` +
    `<div class="ccs-lower-third-text">` +
    `<div class="ccs-lower-third-tag"></div>` +
    `<div class="ccs-lower-third-name"></div>` +
    `<div class="ccs-lower-third-subtitle"></div>` +
    `</div>` +
    `</div>`;
  if (item) updateLowerThird(el, item);
  return el;
}

export function updateLowerThird(el: LowerThirdEl, item: LayoutItem): void {
  applyAppearance(el, item);

  const name = String(prop(item, "name", "") || "");
  const subtitle = String(prop(item, "subtitle", "") || "");
  const tag = String(prop(item, "tag", "") || "");
  const avatarUrl = String(prop(item, "avatarUrl", "") || "");
  const showName = prop(item, "showName", true) !== false;
  const showSubtitle = prop(item, "showSubtitle", true) !== false;
  const showTag = prop(item, "showTag", false) === true;
  const showAvatar = prop(item, "showAvatar", true) !== false;

  const avatarWrap = el.querySelector<HTMLElement>(".ccs-lower-third-avatar-wrap");
  const avatar = el.querySelector<HTMLImageElement>(".ccs-lower-third-avatar");
  const nameEl = el.querySelector<HTMLElement>(".ccs-lower-third-name");
  const subtitleEl = el.querySelector<HTMLElement>(".ccs-lower-third-subtitle");
  const tagEl = el.querySelector<HTMLElement>(".ccs-lower-third-tag");

  const hasContent = Boolean(name || subtitle || tag || avatarUrl);
  el.classList.toggle("has-content", hasContent);

  if (avatarWrap) avatarWrap.style.display = showAvatar && avatarUrl ? "" : "none";
  if (avatar) {
    if (avatarUrl) avatar.src = avatarUrl;
    else avatar.removeAttribute("src");
  }
  if (nameEl) {
    nameEl.style.display = showName && name ? "" : "none";
    nameEl.textContent = name;
  }
  if (subtitleEl) {
    subtitleEl.style.display = showSubtitle && subtitle ? "" : "none";
    subtitleEl.textContent = subtitle;
  }
  if (tagEl) {
    tagEl.style.display = showTag && tag ? "" : "none";
    tagEl.textContent = tag;
  }
}
