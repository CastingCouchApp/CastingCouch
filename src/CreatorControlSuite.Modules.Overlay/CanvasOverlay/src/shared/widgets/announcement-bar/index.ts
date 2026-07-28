import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./announcement-bar.css";

export const ANNOUNCEMENT_BAR_VARIANTS = [
  "classic",
  "neon",
  "glass",
  "cyber",
  "minimal",
  "bold",
  "pill",
  "strip",
  "ribbon",
  "alert-soft",
  "sponsor",
  "schedule"
] as const;

export type AnnouncementBarVariant = (typeof ANNOUNCEMENT_BAR_VARIANTS)[number];

export const ANNOUNCEMENT_BAR_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  slim: { w: 960, h: 48, label: "Slim" },
  standard: { w: 1200, h: 64, label: "Standard" },
  tall: { w: 1100, h: 88, label: "Tall" },
  banner: { w: 1400, h: 72, label: "Banner" }
};

function barVariant(item: LayoutItem | null | undefined): AnnouncementBarVariant {
  const raw = String(prop(item, "variant", "classic") || "classic").toLowerCase();
  return (ANNOUNCEMENT_BAR_VARIANTS as readonly string[]).includes(raw)
    ? (raw as AnnouncementBarVariant)
    : "classic";
}

function syncMarquee(el: HTMLElement, item: LayoutItem, scroll: boolean): void {
  const track = el.querySelector<HTMLElement>(".ccs-announcement-bar-track");
  if (!track) return;
  const inner = track.querySelector<HTMLElement>(".ccs-announcement-bar-marquee-inner");
  if (!inner) return;

  if (!scroll) {
    track.classList.remove("is-scrolling");
    track.style.removeProperty("--ccs-announce-marquee-distance");
    track.style.removeProperty("--ccs-announce-marquee-duration");
    return;
  }

  void track.offsetWidth;
  const overflow = inner.scrollWidth - track.clientWidth;
  const scrolling = overflow > 4;
  track.classList.toggle("is-scrolling", scrolling);
  if (scrolling) {
    const gap = Math.max(24, Number(prop(item, "repeatGap", 48)) || 48);
    track.style.setProperty("--ccs-announce-marquee-distance", overflow + gap + "px");
    const speed = Math.max(10, Number(getComputedStyle(el).getPropertyValue("--ccs-announce-speed")) || 40);
    const duration = Math.max(4, Math.min(40, (overflow + gap) / speed));
    track.style.setProperty("--ccs-announce-marquee-duration", duration + "s");
  }
}

export function applyAnnouncementBar(el: HTMLElement, item: LayoutItem): void {
  const variant = barVariant(item);
  ANNOUNCEMENT_BAR_VARIANTS.forEach((name) => el.classList.remove("ccs-announcement-bar-v-" + name));
  el.classList.add("ccs-announcement-bar-v-" + variant);
  el.dataset.variant = variant;

  const sizeKey = String(prop(item, "sizePreset", "standard") || "standard").toLowerCase();
  Object.keys(ANNOUNCEMENT_BAR_SIZE_PRESETS).forEach((name) => el.classList.remove("ccs-announcement-bar-s-" + name));
  if (ANNOUNCEMENT_BAR_SIZE_PRESETS[sizeKey]) {
    el.classList.add("ccs-announcement-bar-s-" + sizeKey);
  }

  const align = String(prop(item, "align", "center") || "center");
  const direction = String(prop(item, "direction", "ltr") || "ltr").toLowerCase() === "rtl" ? "rtl" : "ltr";
  const scroll = prop(item, "scroll", false) === true;
  const speed = Math.max(10, Number(prop(item, "speed", 40)) || 40);
  const padding = Math.max(0, Number(prop(item, "padding", 16)) || 16);
  const radius = Math.max(0, Number(prop(item, "radius", 12)) || 12);

  el.style.setProperty("--ccs-announce-align", align);
  el.style.setProperty("--ccs-announce-color", String(prop(item, "color", "#ffffff") || "#ffffff"));
  el.style.setProperty("--ccs-announce-color2", String(prop(item, "color2", "#ff7a00") || "#ff7a00"));
  el.style.setProperty("--ccs-announce-bg", String(prop(item, "backgroundColor", "rgba(10,10,16,0.88)") || "rgba(10,10,16,0.88)"));
  el.style.setProperty("--ccs-announce-speed", String(speed));
  el.style.setProperty("--ccs-announce-padding", padding + "px");
  el.style.setProperty("--ccs-announce-radius", radius + "px");
  el.style.setProperty("--ccs-announce-repeat-gap", (Number(prop(item, "repeatGap", 48)) || 48) + "px");

  const font = prop(item, "font", null) as Record<string, unknown> | null;
  if (font) {
    if (font.family) el.style.setProperty("--ccs-announce-font", String(font.family));
    if (font.sizePx) el.style.setProperty("--ccs-announce-size", Number(font.sizePx) + "px");
    if (font.weight) el.style.setProperty("--ccs-announce-weight", String(font.weight));
  }

  el.classList.toggle("ccs-announce-uppercase", prop(item, "uppercase", false) === true);
  el.classList.toggle("ccs-announce-scroll", scroll);
  el.classList.toggle("ccs-announce-rtl", direction === "rtl");
  el.dataset.direction = direction;

  const accent = el.querySelector<HTMLElement>(".ccs-announcement-bar-accent");
  if (accent) accent.style.display = prop(item, "showAccentDot", true) !== false ? "" : "none";

  const iconEl = el.querySelector<HTMLElement>(".ccs-announcement-bar-icon");
  if (iconEl) {
    const showIcon = prop(item, "showIcon", false) === true;
    iconEl.style.display = showIcon ? "" : "none";
    iconEl.textContent = String(prop(item, "icon", "📢") || "📢");
  }

  const prefixEl = el.querySelector<HTMLElement>(".ccs-announcement-bar-prefix");
  if (prefixEl) {
    const showPrefix = prop(item, "showPrefix", false) === true;
    prefixEl.style.display = showPrefix ? "" : "none";
    prefixEl.textContent = String(prop(item, "prefix", "") || "");
  }

  requestAnimationFrame(() => syncMarquee(el, item, scroll));
}

export function createAnnouncementBarEl(item?: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className = "ccs-announcement-bar ccs-announcement-bar-v-classic";
  el.innerHTML =
    `<span class="ccs-announcement-bar-accent" aria-hidden="true"></span>` +
    `<span class="ccs-announcement-bar-icon" aria-hidden="true">📢</span>` +
    `<span class="ccs-announcement-bar-prefix"></span>` +
    `<div class="ccs-announcement-bar-track">` +
    `<div class="ccs-announcement-bar-marquee-inner">` +
    `<span class="ccs-announcement-bar-message"></span>` +
    `</div></div>`;
  if (item) updateAnnouncementBar(el, item);
  return el;
}

export function updateAnnouncementBar(el: HTMLElement, item: LayoutItem): void {
  applyAnnouncementBar(el, item);

  const message = String(prop(item, "message", "") || "").trim();
  const msgEl = el.querySelector<HTMLElement>(".ccs-announcement-bar-message");
  if (msgEl) msgEl.textContent = message;

  const empty = message.length === 0;
  el.classList.toggle("is-empty", empty);
  el.classList.toggle("is-hidden", empty && prop(item, "hideWhenEmpty", true) !== false);

  const scroll = prop(item, "scroll", false) === true;
  requestAnimationFrame(() => syncMarquee(el, item, scroll));
}
