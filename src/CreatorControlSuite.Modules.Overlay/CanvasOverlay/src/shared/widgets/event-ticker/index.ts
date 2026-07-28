import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { applySizeClass, applyVariantClasses, pickVariant } from "../../utils/look";
import "./event-ticker.css";

export const EVENT_TICKER_VARIANTS = [
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

export const EVENT_TICKER_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  slim: { w: 640, h: 40, label: "Slim" },
  standard: { w: 900, h: 52, label: "Standard" },
  tall: { w: 900, h: 72, label: "Tall" },
  banner: { w: 1100, h: 48, label: "Banner" }
};

export const EVENT_TICKER_SOURCES = [
  "follow",
  "subscribe",
  "gift",
  "cheer",
  "raid",
  "tip",
  "host",
  "redemption",
  "chat",
  "custom"
] as const;

const SIZE_KEYS = Object.keys(EVENT_TICKER_SIZE_PRESETS);

export type EventTickerItem = {
  id?: string;
  type?: string;
  text?: string;
  icon?: string;
  avatarUrl?: string;
  time?: string | number;
  [key: string]: unknown;
};

type EventTickerEl = HTMLElement & {
  _events?: EventTickerItem[];
  _cycleIndex?: number;
  _cycleTimer?: ReturnType<typeof setInterval> | null;
};

const SOURCE_ICONS: Record<string, string> = {
  follow: "★",
  subscribe: "♥",
  gift: "🎁",
  cheer: "✦",
  raid: "⚔",
  tip: "$",
  host: "⌂",
  redemption: "✺",
  chat: "💬",
  custom: "•"
};

function normalizeSources(raw: unknown): string[] {
  if (!Array.isArray(raw) || raw.length === 0) {
    return [...EVENT_TICKER_SOURCES];
  }
  return raw
    .map((entry) => String(entry || "").toLowerCase())
    .filter((entry) => (EVENT_TICKER_SOURCES as readonly string[]).includes(entry));
}

function formatTime(raw: unknown): string {
  if (raw == null || raw === "") return "";
  if (typeof raw === "number") {
    const d = new Date(raw);
    return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
  }
  const text = String(raw).trim();
  if (/^\d{10,13}$/.test(text)) {
    const ms = text.length > 10 ? Number(text) : Number(text) * 1000;
    const d = new Date(ms);
    return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
  }
  return text;
}

function renderItemHtml(event: EventTickerItem, item: LayoutItem): string {
  const showIcon = prop(item, "showIcon", true) !== false;
  const showType = prop(item, "showType", false) === true;
  const showTime = prop(item, "showTime", false) === true;
  const showAvatars = prop(item, "showAvatars", false) === true;
  const uppercase = prop(item, "uppercase", false) === true;
  const template = String(prop(item, "template", "{text}") || "{text}");
  const type = String(event.type || "custom").toLowerCase();
  const text = String(event.text || "").trim();
  const icon = String(event.icon || SOURCE_ICONS[type] || "•");
  const time = formatTime(event.time);
  const avatarUrl = String(event.avatarUrl || "");

  let body = template
    .replace(/\{text\}/g, text)
    .replace(/\{type\}/g, type)
    .replace(/\{icon\}/g, icon)
    .replace(/\{time\}/g, time);

  if (uppercase) body = body.toUpperCase();

  const parts: string[] = [];
  if (showAvatars && avatarUrl) {
    parts.push(`<img class="ccs-event-ticker-avatar" src="${avatarUrl}" alt="" draggable="false" />`);
  }
  if (showIcon) {
    parts.push(`<span class="ccs-event-ticker-icon">${icon}</span>`);
  }
  if (showType && type) {
    parts.push(`<span class="ccs-event-ticker-type">${type}</span>`);
  }
  parts.push(`<span class="ccs-event-ticker-text">${body}</span>`);
  if (showTime && time) {
    parts.push(`<span class="ccs-event-ticker-time">${time}</span>`);
  }
  return `<span class="ccs-event-ticker-item" data-type="${type}">${parts.join("")}</span>`;
}

function clearCycle(el: EventTickerEl): void {
  if (el._cycleTimer != null) {
    clearInterval(el._cycleTimer);
    el._cycleTimer = null;
  }
}

function applyAppearance(el: EventTickerEl, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "classic"), EVENT_TICKER_VARIANTS);
  const sizeKey = pickVariant(prop(item, "sizePreset", "standard"), SIZE_KEYS, "standard");
  applyVariantClasses(el, "ccs-event-ticker-v-", variant, EVENT_TICKER_VARIANTS);
  applySizeClass(el, "ccs-event-ticker-s-", sizeKey, SIZE_KEYS);

  const color = String(prop(item, "color", "") || "");
  const bg = String(prop(item, "backgroundColor", "") || "");
  const fontFamily = String(prop(item, "fontFamily", "") || "");
  const fontSizePx = Number(prop(item, "fontSizePx", 0)) || 0;
  const gapPx = Number(prop(item, "gapPx", 0)) || 0;
  const paddingPx = Number(prop(item, "paddingPx", 0)) || 0;
  const radiusPx = Number(prop(item, "borderRadiusPx", 0)) || 0;
  const speed = Math.max(4, Number(prop(item, "speed", 24)) || 24);
  const mode = String(prop(item, "mode", "marquee") || "marquee");

  if (color) el.style.setProperty("--ccs-ticker-color", color);
  else el.style.removeProperty("--ccs-ticker-color");
  if (bg) el.style.setProperty("--ccs-ticker-bg", bg);
  else el.style.removeProperty("--ccs-ticker-bg");
  if (fontFamily) el.style.setProperty("--ccs-ticker-font", fontFamily);
  else el.style.removeProperty("--ccs-ticker-font");
  if (fontSizePx) el.style.setProperty("--ccs-ticker-font-size", fontSizePx + "px");
  else el.style.removeProperty("--ccs-ticker-font-size");
  if (gapPx) el.style.setProperty("--ccs-ticker-gap", gapPx + "px");
  else el.style.removeProperty("--ccs-ticker-gap");
  if (paddingPx) el.style.setProperty("--ccs-ticker-pad", paddingPx + "px");
  else el.style.removeProperty("--ccs-ticker-pad");
  if (radiusPx || radiusPx === 0) el.style.setProperty("--ccs-ticker-radius", radiusPx + "px");
  else el.style.removeProperty("--ccs-ticker-radius");
  el.style.setProperty("--ccs-ticker-speed", speed + "s");

  el.classList.remove("ccs-event-ticker-mode-marquee", "ccs-event-ticker-mode-fade-cycle", "ccs-event-ticker-mode-static-list");
  el.classList.add("ccs-event-ticker-mode-" + (["marquee", "fade-cycle", "static-list"].includes(mode) ? mode : "marquee"));
}

function paintTicker(el: EventTickerEl, item: LayoutItem): void {
  const events = el._events || [];
  const hideWhenEmpty = prop(item, "hideWhenEmpty", true) !== false;
  const separator = String(prop(item, "separator", "  •  ") || "  •  ");
  const viewport = el.querySelector<HTMLElement>(".ccs-event-ticker-viewport");
  const track = el.querySelector<HTMLElement>(".ccs-event-ticker-track");
  const list = el.querySelector<HTMLElement>(".ccs-event-ticker-list");
  if (!viewport || !track || !list) return;

  el.classList.toggle("is-empty", events.length === 0);
  el.classList.toggle("is-hidden", hideWhenEmpty && events.length === 0);

  const html = events.map((evt) => renderItemHtml(evt, item)).join(`<span class="ccs-event-ticker-sep">${separator}</span>`);
  const mode = String(prop(item, "mode", "marquee") || "marquee");
  if (mode === "marquee" && events.length > 0) {
    track.innerHTML = html + `<span class="ccs-event-ticker-sep">${separator}</span>` + html;
  } else {
    track.innerHTML = html;
  }
  list.innerHTML = events.map((evt) => `<li>${renderItemHtml(evt, item)}</li>`).join("");

  clearCycle(el);
  if (mode === "fade-cycle" && events.length > 1) {
    el._cycleIndex = 0;
    const cycle = () => {
      const idx = el._cycleIndex || 0;
      track.querySelectorAll<HTMLElement>(".ccs-event-ticker-item").forEach((node, i) => {
        node.classList.toggle("is-active", i === idx);
      });
      el._cycleIndex = (idx + 1) % events.length;
    };
    cycle();
    el._cycleTimer = setInterval(cycle, Math.max(1200, Number(prop(item, "speed", 24)) * 100));
  }
}

export function createEventTickerEl(item?: LayoutItem): EventTickerEl {
  const el = document.createElement("div") as EventTickerEl;
  el.className =
    "ccs-event-ticker ccs-event-ticker-v-classic ccs-event-ticker-s-standard ccs-event-ticker-mode-marquee";
  el.innerHTML =
    `<div class="ccs-event-ticker-viewport">` +
    `<div class="ccs-event-ticker-track"></div>` +
    `</div>` +
    `<ul class="ccs-event-ticker-list"></ul>`;
  el._events = [];
  if (item) updateEventTicker(el, item);
  return el;
}

export function pushEventTickerItem(
  el: EventTickerEl,
  layoutItem: LayoutItem,
  event: EventTickerItem
): void {
  const sources = normalizeSources(prop(layoutItem, "sources", null));
  const type = String(event.type || "custom").toLowerCase();
  if (!sources.includes(type)) return;

  const maxItems = Math.max(1, Number(prop(layoutItem, "maxItems", 20)) || 20);
  const order = String(prop(layoutItem, "order", "newest-first") || "newest-first");
  const queue = el._events ? [...el._events] : [];
  if (order === "oldest-first") queue.push(event);
  else queue.unshift(event);
  el._events = queue.slice(0, maxItems);
  paintTicker(el, layoutItem);
}

export function updateEventTicker(
  el: EventTickerEl,
  item: LayoutItem,
  _data?: Record<string, unknown> | null
): void {
  applyAppearance(el, item);
  if (!el._events) el._events = [];
  const maxItems = Math.max(1, Number(prop(item, "maxItems", 20)) || 20);
  el._events = el._events.slice(0, maxItems);
  paintTicker(el, item);
}
