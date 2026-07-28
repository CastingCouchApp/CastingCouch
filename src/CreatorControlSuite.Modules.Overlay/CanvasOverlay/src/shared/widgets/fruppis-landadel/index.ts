import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { applySizeClass, applyVariantClasses, pickVariant } from "../../utils/look";
import { rgbaFrom } from "../../utils/color";
import "./fruppis-landadel.css";

export const FRUPPIS_LANDADEL_VARIANTS = [
  "gentry",
  "counsel",
  "cambridge",
  "hoodie",
  "estate",
  "shadow",
  "ivory",
  "crimson",
  "split",
  "crest",
  "dossier",
  "portrait",
  "ribbon",
  "glass",
  "neon",
  "minimal"
] as const;

export type FruppisLandadelVariant = (typeof FRUPPIS_LANDADEL_VARIANTS)[number];

export const FRUPPIS_LANDADEL_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  compact: { w: 420, h: 148, label: "Compact" },
  standard: { w: 560, h: 200, label: "Standard" },
  wide: { w: 760, h: 200, label: "Wide" },
  banner: { w: 960, h: 160, label: "Banner" },
  tall: { w: 480, h: 280, label: "Tall" },
  portrait: { w: 360, h: 520, label: "Portrait" }
};

export const FRUPPIS_LANDADEL_LAYOUTS = [
  "figure-left",
  "figure-right",
  "stacked",
  "banner"
] as const;

export const FRUPPIS_LANDADEL_MOODS = ["shady", "polished", "casual"] as const;

const SIZE_KEYS = Object.keys(FRUPPIS_LANDADEL_SIZE_PRESETS);
const LAYOUTS = FRUPPIS_LANDADEL_LAYOUTS as readonly string[];
const MOODS = FRUPPIS_LANDADEL_MOODS as readonly string[];

type FruppisEl = HTMLElement;

function setVar(el: FruppisEl, name: string, value: string | number | null | undefined, unit = ""): void {
  if (value === null || value === undefined || value === "") {
    el.style.removeProperty(name);
    return;
  }
  el.style.setProperty(name, typeof value === "number" ? value + unit : String(value));
}

function applyAppearance(el: FruppisEl, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "gentry"), FRUPPIS_LANDADEL_VARIANTS, "gentry");
  const sizeKey = pickVariant(prop(item, "sizePreset", "standard"), SIZE_KEYS, "standard");
  applyVariantClasses(el, "ccs-fruppis-landadel-v-", variant, FRUPPIS_LANDADEL_VARIANTS);
  applySizeClass(el, "ccs-fruppis-landadel-s-", sizeKey, SIZE_KEYS);

  const layoutRaw = String(prop(item, "layout", "figure-left") || "figure-left");
  const layout = LAYOUTS.includes(layoutRaw) ? layoutRaw : "figure-left";
  LAYOUTS.forEach((name) => el.classList.remove("ccs-fruppis-landadel-layout-" + name));
  el.classList.add("ccs-fruppis-landadel-layout-" + layout);

  const moodRaw = String(prop(item, "mood", "shady") || "shady");
  const mood = MOODS.includes(moodRaw) ? moodRaw : "shady";
  MOODS.forEach((name) => el.classList.remove("ccs-fruppis-landadel-mood-" + name));
  el.classList.add("ccs-fruppis-landadel-mood-" + mood);

  const entranceRaw = String(prop(item, "entrance", "none") || "none");
  const entrance = ["none", "slide", "fade", "pop"].includes(entranceRaw) ? entranceRaw : "none";
  ["none", "slide", "fade", "pop"].forEach((name) =>
    el.classList.remove("ccs-fruppis-landadel-entrance-" + name)
  );
  el.classList.add("ccs-fruppis-landadel-entrance-" + entrance);

  el.classList.toggle("ccs-fruppis-landadel-side-part", prop(item, "showSidePart", true) !== false);
  el.classList.toggle("ccs-fruppis-landadel-uppercase", prop(item, "uppercaseName", false) === true);
  el.classList.toggle("ccs-fruppis-landadel-accent-bar", prop(item, "showAccentBar", true) !== false);

  const rawShade = Number(prop(item, "shadeIntensity", 0.45));
  const shade = Number.isFinite(rawShade) ? Math.max(0, Math.min(1, rawShade)) : 0.45;
  setVar(el, "--ccs-fl-shade", shade);

  setVar(el, "--ccs-fl-hoodie", String(prop(item, "hoodieColor", "#2E6BB0") || "#2E6BB0"));
  setVar(el, "--ccs-fl-pants", String(prop(item, "pantsColor", "#B91C3A") || "#B91C3A"));
  setVar(el, "--ccs-fl-shoes", String(prop(item, "shoeColor", "#F2EEE6") || "#F2EEE6"));
  setVar(el, "--ccs-fl-hair", String(prop(item, "hairColor", "#E8D5A3") || "#E8D5A3"));
  setVar(el, "--ccs-fl-eyes", String(prop(item, "eyeColor", "#4A90D9") || "#4A90D9"));
  setVar(el, "--ccs-fl-skin", String(prop(item, "skinColor", "#E8C4A8") || "#E8C4A8"));
  setVar(el, "--ccs-fl-accent", String(prop(item, "color", "#2E6BB0") || "#2E6BB0"));
  setVar(el, "--ccs-fl-accent2", String(prop(item, "color2", "#B91C3A") || "#B91C3A"));
  setVar(el, "--ccs-fl-text", String(prop(item, "textColor", "#F2EEE6") || "#F2EEE6"));
  setVar(el, "--ccs-fl-muted", String(prop(item, "subtitleColor", "#C8B890") || "#C8B890"));
  setVar(el, "--ccs-fl-tag", String(prop(item, "tagColor", "#E8D5A3") || "#E8D5A3"));
  setVar(el, "--ccs-fl-quote", String(prop(item, "quoteColor", "#A8B8C8") || "#A8B8C8"));

  const bgColor = String(prop(item, "bgColor", "#080C12") || "#080C12");
  const bgOpacity = Number(prop(item, "bgOpacity", 0.82));
  const opacity = Number.isFinite(bgOpacity) ? Math.max(0, Math.min(1, bgOpacity)) : 0.82;
  setVar(el, "--ccs-fl-bg", rgbaFrom(bgColor, opacity));

  const nameFont = String(prop(item, "nameFontFamily", "") || prop(item, "fontFamily", "") || "");
  const titleFont = String(prop(item, "titleFontFamily", "") || "");
  const quoteFont = String(prop(item, "quoteFontFamily", "") || "");
  setVar(el, "--ccs-fl-name-font", nameFont || null);
  setVar(el, "--ccs-fl-title-font", titleFont || null);
  setVar(el, "--ccs-fl-quote-font", quoteFont || null);

  const nameSize = Number(prop(item, "nameSizePx", 0)) || 0;
  const titleSize = Number(prop(item, "titleSizePx", 0)) || 0;
  const quoteSize = Number(prop(item, "quoteSizePx", 0)) || 0;
  setVar(el, "--ccs-fl-name-size", nameSize || null, "px");
  setVar(el, "--ccs-fl-title-size", titleSize || null, "px");
  setVar(el, "--ccs-fl-quote-size", quoteSize || null, "px");

  const radius = Number(prop(item, "borderRadiusPx", 14));
  const padding = Number(prop(item, "paddingPx", 16));
  const gap = Number(prop(item, "gapPx", 14));
  setVar(el, "--ccs-fl-radius", Number.isFinite(radius) ? radius : 14, "px");
  setVar(el, "--ccs-fl-pad", Number.isFinite(padding) ? padding : 16, "px");
  setVar(el, "--ccs-fl-gap", Number.isFinite(gap) ? gap : 14, "px");
}

export function createFruppisLandadelEl(item?: LayoutItem): FruppisEl {
  const el = document.createElement("div") as FruppisEl;
  el.className =
    "ccs-fruppis-landadel ccs-fruppis-landadel-v-gentry ccs-fruppis-landadel-s-standard " +
    "ccs-fruppis-landadel-layout-figure-left ccs-fruppis-landadel-mood-shady " +
    "ccs-fruppis-landadel-entrance-none ccs-fruppis-landadel-side-part ccs-fruppis-landadel-accent-bar";
  el.innerHTML =
    `<div class="ccs-fruppis-landadel-accent" aria-hidden="true"></div>` +
    `<div class="ccs-fruppis-landadel-inner">` +
    `<div class="ccs-fruppis-landadel-figure" aria-hidden="true">` +
    `<div class="ccs-fruppis-landadel-head">` +
    `<div class="ccs-fruppis-landadel-hair"></div>` +
    `<div class="ccs-fruppis-landadel-face">` +
    `<span class="ccs-fruppis-landadel-eye ccs-fl-eye-l"></span>` +
    `<span class="ccs-fruppis-landadel-eye ccs-fl-eye-r"></span>` +
    `</div>` +
    `</div>` +
    `<div class="ccs-fruppis-landadel-hoodie"></div>` +
    `<div class="ccs-fruppis-landadel-pants"></div>` +
    `<div class="ccs-fruppis-landadel-shoes">` +
    `<span></span><span></span>` +
    `</div>` +
    `</div>` +
    `<div class="ccs-fruppis-landadel-avatar-wrap">` +
    `<img class="ccs-fruppis-landadel-avatar" alt="" draggable="false" />` +
    `</div>` +
    `<div class="ccs-fruppis-landadel-text">` +
    `<div class="ccs-fruppis-landadel-tag"></div>` +
    `<div class="ccs-fruppis-landadel-name"></div>` +
    `<div class="ccs-fruppis-landadel-title"></div>` +
    `<div class="ccs-fruppis-landadel-subtitle"></div>` +
    `<div class="ccs-fruppis-landadel-quote"></div>` +
    `<div class="ccs-fruppis-landadel-stats"></div>` +
    `</div>` +
    `</div>`;
  if (item) updateFruppisLandadel(el, item);
  return el;
}

export function updateFruppisLandadel(el: FruppisEl, item: LayoutItem): void {
  applyAppearance(el, item);

  const name = String(prop(item, "name", "") || "");
  const title = String(prop(item, "title", "") || "");
  const subtitle = String(prop(item, "subtitle", "") || "");
  const tag = String(prop(item, "tag", "") || "");
  const quote = String(prop(item, "quote", "") || "");
  const stats = String(prop(item, "stats", "") || "");
  const avatarUrl = String(prop(item, "avatarUrl", "") || "");

  const showFigure = prop(item, "showFigure", true) !== false;
  const showAvatar = prop(item, "showAvatar", false) === true;
  const showName = prop(item, "showName", true) !== false;
  const showTitle = prop(item, "showTitle", true) !== false;
  const showSubtitle = prop(item, "showSubtitle", true) !== false;
  const showTag = prop(item, "showTag", true) !== false;
  const showQuote = prop(item, "showQuote", true) !== false;
  const showStats = prop(item, "showStats", true) !== false;

  const figure = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-figure");
  const avatarWrap = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-avatar-wrap");
  const avatar = el.querySelector<HTMLImageElement>(".ccs-fruppis-landadel-avatar");
  const nameEl = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-name");
  const titleEl = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-title");
  const subtitleEl = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-subtitle");
  const tagEl = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-tag");
  const quoteEl = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-quote");
  const statsEl = el.querySelector<HTMLElement>(".ccs-fruppis-landadel-stats");

  const hasContent = Boolean(name || title || subtitle || tag || quote || stats || avatarUrl);
  el.classList.toggle("has-content", hasContent);
  el.classList.toggle(
    "ccs-fruppis-landadel-hide-empty",
    prop(item, "hideWhenEmpty", false) === true && !hasContent
  );

  if (figure) figure.style.display = showFigure ? "" : "none";
  if (avatarWrap) avatarWrap.style.display = showAvatar && avatarUrl ? "" : "none";
  if (avatar) {
    if (avatarUrl) avatar.src = avatarUrl;
    else avatar.removeAttribute("src");
  }
  if (nameEl) {
    nameEl.style.display = showName && name ? "" : "none";
    nameEl.textContent = name;
  }
  if (titleEl) {
    titleEl.style.display = showTitle && title ? "" : "none";
    titleEl.textContent = title;
  }
  if (subtitleEl) {
    subtitleEl.style.display = showSubtitle && subtitle ? "" : "none";
    subtitleEl.textContent = subtitle;
  }
  if (tagEl) {
    tagEl.style.display = showTag && tag ? "" : "none";
    tagEl.textContent = tag;
  }
  if (quoteEl) {
    quoteEl.style.display = showQuote && quote ? "" : "none";
    quoteEl.textContent = quote;
  }
  if (statsEl) {
    statsEl.style.display = showStats && stats ? "" : "none";
    statsEl.textContent = stats;
  }
}
