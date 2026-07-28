import { escapeHtml } from "../../utils/html";
import { prop } from "../../utils/prop";
import "./chat.css";

export { escapeHtml } from "../../utils/html";

export const CHAT_EVENT_TYPES = new Set([
  "channel.follow",
  "channel.subscribe",
  "channel.subscription.message",
  "channel.subscription.gift",
  "channel.cheer",
  "channel.raid",
  "stream.online",
  "stream.offline"
]);

export const CHAT_VARIANTS = [
  "classic",
  "compact",
  "bubbles",
  "neon",
  "glass",
  "minimal",
  "hud",
  "outline",
  "soft",
  "strip"
] as const;

export type ChatVariant = (typeof CHAT_VARIANTS)[number];

export const CHAT_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  slim: { w: 320, h: 480, label: "Slim" },
  standard: { w: 420, h: 560, label: "Standard" },
  tall: { w: 420, h: 720, label: "Tall" },
  wide: { w: 560, h: 480, label: "Wide" },
  banner: { w: 720, h: 280, label: "Banner" },
  "obs-side": { w: 360, h: 640, label: "OBS Side" }
};

export const TWITCH_DEFAULT_COLORS = [
  "#FF0000",
  "#0000FF",
  "#00FF00",
  "#B22222",
  "#FF7F50",
  "#9ACD32",
  "#FF4500",
  "#2E8B57",
  "#DAA520",
  "#D2691E",
  "#5F9EA0",
  "#1E90FF",
  "#FF69B4",
  "#8A2BE2",
  "#00FF7F"
] as const;

const CHAT_EVENT_ICONS: Record<string, string> = {
  "channel.follow": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
  "channel.subscribe": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
  "channel.subscription.message": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
  "channel.subscription.gift": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
  "channel.cheer": "https://static-cdn.jtvnw.net/badges/v1/73b5c3fb-7f24-432c-a4ae-c6c3d5e3d5c7/2",
  "channel.raid": "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85b62/2",
  "stream.online": "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/2",
  "stream.offline": "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/2"
};

type ChatEl = HTMLElement & {
  _lines?: HTMLElement | null;
  _seenMessageIds?: Set<string>;
  _showTwitchEvents?: boolean;
  _showEventIcons?: boolean;
  _showBadges?: boolean;
  _showEmotes?: boolean;
  _showTimestamps?: boolean;
  _timestampFormat?: string;
  _hideCommands?: boolean;
  _nameDisplay?: string;
  _separator?: string;
  _uppercaseNames?: boolean;
  _showStatusLine?: boolean;
  _useTwitchUserColor?: boolean;
  _fallbackUserColor?: string;
  _animateNewLines?: boolean;
  _fadeOut?: boolean;
  _fadeAfterMs?: number;
  _fadeDurationMs?: number;
  _maxLines?: number;
  _fadeTimers?: Map<string, number>;
};

function normalizeHexColor(value: unknown): string | null {
  const raw = String(value ?? "").trim();
  const match = /^#?([0-9a-fA-F]{6})$/.exec(raw);
  if (!match) return null;
  return `#${match[1].toUpperCase()}`;
}

export function resolveChatUserColor(
  color: unknown,
  login: unknown,
  opts?: { useTwitchUserColor?: boolean; fallbackUserColor?: string }
): string {
  const useTwitch = opts?.useTwitchUserColor !== false;
  const fallback = normalizeHexColor(opts?.fallbackUserColor) || "#DEDEDE";
  if (!useTwitch) {
    return fallback;
  }
  const hex = normalizeHexColor(color);
  if (hex) return hex;
  const name = String(login || "user").trim() || "user";
  const n = name.charCodeAt(0) + name.charCodeAt(name.length - 1);
  return TWITCH_DEFAULT_COLORS[n % TWITCH_DEFAULT_COLORS.length];
}

function chatVariant(item: unknown): ChatVariant {
  const raw = String(prop(item as never, "variant", "classic") || "classic").toLowerCase();
  return (CHAT_VARIANTS as readonly string[]).includes(raw) ? (raw as ChatVariant) : "classic";
}

function boolPropValue(props: Record<string, unknown>, key: string, fallback: boolean): boolean {
  if (props[key] === undefined || props[key] === null || props[key] === "") return fallback;
  return props[key] !== false && props[key] !== "false" && props[key] !== 0;
}

function partsPlainText(partsJson: unknown): string {
  try {
    const parts = JSON.parse(String(partsJson || "[]"));
    if (!Array.isArray(parts)) return "";
    return parts.map((p) => String(p?.text || "")).join("");
  } catch {
    return "";
  }
}

export function renderChatParts(partsJson: unknown, showEmotes = true): string {
  let parts: Array<{ type?: string; text?: string; url?: string }> = [];
  try {
    parts = JSON.parse(String(partsJson || "[]"));
  } catch {
    return "";
  }
  if (!Array.isArray(parts)) return "";

  return parts
    .map((part) => {
      if (showEmotes && part.type === "emote" && part.url) {
        const alt = escapeHtml(part.text || "");
        return `<img class="ccs-chat-emote" src="${escapeHtml(part.url)}" alt="${alt}" title="${alt}" />`;
      }
      return escapeHtml(part.text || "");
    })
    .join("");
}

export function renderChatBadges(badgesJson: unknown): string {
  let badges: Array<{ url?: string; title?: string; setId?: string }> = [];
  try {
    const parsed = JSON.parse(String(badgesJson || "[]"));
    badges = Array.isArray(parsed) ? parsed : [];
  } catch {
    return "";
  }

  return badges
    .filter((badge) => badge && badge.url)
    .map((badge) => {
      const title = escapeHtml(badge.title || badge.setId || "");
      return `<img class="ccs-chat-badge" src="${escapeHtml(badge.url || "")}" alt="" title="${title}" />`;
    })
    .join("");
}

function formatTimestamp(at: unknown, format: string): string {
  if (!at) return "";
  const date = new Date(String(at));
  if (Number.isNaN(date.getTime())) return "";
  const hh = String(date.getHours()).padStart(2, "0");
  const mm = String(date.getMinutes()).padStart(2, "0");
  const ss = String(date.getSeconds()).padStart(2, "0");
  return format === "hh:mm:ss" ? `${hh}:${mm}:${ss}` : `${hh}:${mm}`;
}

function separatorText(separator: string): string {
  switch (separator) {
    case "dash":
      return " -";
    case "pipe":
      return " |";
    case "none":
      return "";
    default:
      return ":";
  }
}

function displayName(data: Record<string, unknown>, mode: string): string {
  const display = String(data.userName || data.userLogin || "user");
  const login = String(data.userLogin || data.userName || "user");
  if (mode === "login") return login;
  if (mode === "both") return `${display} (${login})`;
  return display;
}

function scheduleFade(el: ChatEl, line: HTMLElement): void {
  if (!el._fadeOut) return;
  const after = Math.max(500, Number(el._fadeAfterMs) || 15000);
  const duration = Math.max(100, Number(el._fadeDurationMs) || 800);
  el.style.setProperty("--ccs-chat-fade-duration", `${duration}ms`);
  el.classList.add("ccs-chat-fade");
  el._fadeTimers = el._fadeTimers || new Map();
  const key = line.dataset.messageId || `line-${Math.random()}`;
  const existing = el._fadeTimers.get(key);
  if (existing) window.clearTimeout(existing);
  const timer = window.setTimeout(() => {
    line.classList.add("is-fading");
    window.setTimeout(() => {
      if (line.parentElement) line.parentElement.removeChild(line);
      if (line.dataset.messageId && el._seenMessageIds) {
        el._seenMessageIds.delete(line.dataset.messageId);
      }
      el._fadeTimers?.delete(key);
    }, duration);
  }, after);
  el._fadeTimers.set(key, timer);
}

export function createChatEl(item: unknown): ChatEl {
  const el = document.createElement("div") as ChatEl;
  el.className = "ccs-chat";
  el.innerHTML =
    `<div class="ccs-chat-bg"></div>` +
    `<div class="ccs-chat-lines"><div class="ccs-chat-line ccs-chat-status">Chat bereit</div></div>`;
  el._lines = el.querySelector(".ccs-chat-lines");
  el._seenMessageIds = new Set();
  updateChat(el, item, null);
  return el;
}

export function resolveChatAppearance(item: unknown, chatConfig: unknown) {
  const cfg = (chatConfig || {}) as Record<string, unknown>;
  const props = ((item as { props?: Record<string, unknown> } | null)?.props || {}) as Record<string, unknown>;
  let opacity: number;
  if (props.backgroundOpacityPercent != null && props.backgroundOpacityPercent !== "") {
    opacity = Number(props.backgroundOpacityPercent) / 100;
  } else if (props.backgroundOpacity != null && props.backgroundOpacity !== "") {
    opacity = Number(props.backgroundOpacity);
  } else if (cfg.backgroundOpacity != null) {
    opacity = Number(cfg.backgroundOpacity);
  } else {
    opacity = 0.55;
  }

  const fontSize = Math.min(
    72,
    Math.max(8, Number(props.fontSizePx != null ? props.fontSizePx : (cfg.fontSizePx ?? 18)) || 18)
  );
  const emoteScale = Math.max(0.5, Math.min(3, Number(props.emoteScale != null ? props.emoteScale : 1.55) || 1.55));
  const badgeScale = Math.max(0.5, Math.min(3, Number(props.badgeScale != null ? props.badgeScale : 1) || 1));
  const bubbleOpacity = Math.min(
    1,
    Math.max(0, Number(props.bubbleOpacityPercent != null ? props.bubbleOpacityPercent : 45) / 100)
  );
  const bubbleColor = normalizeHexColor(props.bubbleBgColor) || "#000000";

  return {
    showTwitchEvents: props.showTwitchEvents !== undefined
      ? props.showTwitchEvents !== false
      : cfg.showTwitchEvents !== false,
    showEventIcons: boolPropValue(props, "showEventIcons", true),
    showBadges: boolPropValue(props, "showBadges", true),
    showEmotes: boolPropValue(props, "showEmotes", true),
    showTimestamps: boolPropValue(props, "showTimestamps", false),
    timestampFormat: String(props.timestampFormat || "hh:mm"),
    hideCommands: boolPropValue(props, "hideCommands", false),
    nameDisplay: String(props.nameDisplay || "display"),
    separator: String(props.separator || "colon"),
    uppercaseNames: boolPropValue(props, "uppercaseNames", false),
    showStatusLine: boolPropValue(props, "showStatusLine", true),
    useTwitchUserColor: boolPropValue(props, "useTwitchUserColor", true),
    fallbackUserColor: normalizeHexColor(props.fallbackUserColor) || "#DEDEDE",
    animateNewLines: boolPropValue(props, "animateNewLines", true),
    fadeOut: boolPropValue(props, "fadeOut", false),
    fadeAfterMs: Math.max(500, Number(props.fadeAfterMs != null ? props.fadeAfterMs : 15000) || 15000),
    fadeDurationMs: Math.max(100, Number(props.fadeDurationMs != null ? props.fadeDurationMs : 800) || 800),
    maxLines: Math.max(1, Number(props.maxLines != null ? props.maxLines : 80) || 80),
    variant: chatVariant(item),
    sizePreset: String(props.sizePreset || "standard").toLowerCase(),
    backgroundType: String(props.backgroundType || cfg.backgroundType || "None"),
    backgroundColor: String(props.backgroundColor || cfg.backgroundColor || "#000000"),
    backgroundOpacity: Math.min(1, Math.max(0, opacity)),
    paddingPx: Math.max(0, Number(props.paddingPx != null ? props.paddingPx : (cfg.paddingPx ?? 12)) || 0),
    borderRadiusPx: Math.max(
      0,
      Number(props.borderRadiusPx != null ? props.borderRadiusPx : (cfg.borderRadiusPx ?? 12)) || 0
    ),
    gapPx: Math.max(0, Number(props.gapPx != null ? props.gapPx : (cfg.gapPx ?? 6)) || 0),
    fontSizePx: fontSize,
    fontFamily:
      String(props.fontFamily || cfg.fontFamily || "Segoe UI, system-ui, sans-serif").trim() ||
      "Segoe UI, system-ui, sans-serif",
    fontWeight: String(props.fontWeight || "600"),
    lineHeight: Math.max(0.8, Math.min(3, Number(props.lineHeight != null ? props.lineHeight : 1.35) || 1.35)),
    messageColor: normalizeHexColor(props.messageColor) || "#F5F5F5",
    eventColor: normalizeHexColor(props.eventColor) || "#7DD3FC",
    emoteScale,
    badgeScale,
    bubbleBg: `rgba(${parseInt(bubbleColor.slice(1, 3), 16)}, ${parseInt(bubbleColor.slice(3, 5), 16)}, ${parseInt(bubbleColor.slice(5, 7), 16)}, ${bubbleOpacity})`,
    bubbleRadiusPx: Math.max(0, Number(props.bubbleRadiusPx != null ? props.bubbleRadiusPx : 10) || 0),
    backgroundVersion: String(cfg.backgroundVersion || "0")
  };
}

export function applyChatAppearance(el: ChatEl, appearance: ReturnType<typeof resolveChatAppearance>): void {
  const cfg = appearance || ({} as ReturnType<typeof resolveChatAppearance>);
  const type = String(cfg.backgroundType || "None");
  const opacity = Math.min(1, Math.max(0, Number(cfg.backgroundOpacity ?? 0.55)));
  const fontSize = Math.min(72, Math.max(8, Number(cfg.fontSizePx ?? 18) || 18));

  el.style.setProperty("--ccs-chat-padding", `${Math.max(0, Number(cfg.paddingPx ?? 12))}px`);
  el.style.setProperty("--ccs-chat-radius", `${Math.max(0, Number(cfg.borderRadiusPx ?? 12))}px`);
  el.style.setProperty("--ccs-chat-gap", `${Math.max(0, Number(cfg.gapPx ?? 6))}px`);
  el.style.setProperty("--ccs-chat-bg-opacity", String(opacity));
  el.style.setProperty("--ccs-chat-bg-color", String(cfg.backgroundColor || "#000000"));
  el.style.setProperty("--ccs-chat-font-size", `${fontSize}px`);
  el.style.setProperty("--ccs-chat-font-family", String(cfg.fontFamily || "Segoe UI, system-ui, sans-serif"));
  el.style.setProperty("--ccs-chat-font-weight", String(cfg.fontWeight || "600"));
  el.style.setProperty("--ccs-chat-line-height", String(cfg.lineHeight ?? 1.35));
  el.style.setProperty("--ccs-chat-message-color", String(cfg.messageColor || "#F5F5F5"));
  el.style.setProperty("--ccs-chat-event-color", String(cfg.eventColor || "#7DD3FC"));
  el.style.setProperty("--ccs-chat-emote-size", `${Math.round(fontSize * (cfg.emoteScale || 1.55))}px`);
  el.style.setProperty("--ccs-chat-badge-size", `${Math.round(fontSize * (cfg.badgeScale || 1))}px`);
  el.style.setProperty("--ccs-chat-bubble-bg", String(cfg.bubbleBg || "rgba(0,0,0,0.45)"));
  el.style.setProperty("--ccs-chat-bubble-radius", `${Math.max(0, Number(cfg.bubbleRadiusPx ?? 10))}px`);

  CHAT_VARIANTS.forEach((name) => el.classList.remove(`ccs-chat-v-${name}`));
  el.classList.add(`ccs-chat-v-${cfg.variant || "classic"}`);
  Object.keys(CHAT_SIZE_PRESETS).forEach((name) => el.classList.remove(`ccs-chat-s-${name}`));
  if (CHAT_SIZE_PRESETS[cfg.sizePreset]) {
    el.classList.add(`ccs-chat-s-${cfg.sizePreset}`);
  }

  el.classList.toggle("ccs-chat-uppercase-names", !!cfg.uppercaseNames);
  el.classList.toggle("ccs-chat-animate", cfg.animateNewLines !== false);
  el.classList.toggle("ccs-chat-fade", !!cfg.fadeOut);

  el.classList.remove("has-bg", "bg-image");
  if (type === "Color") {
    el.classList.add("has-bg");
  } else if (type === "Image") {
    el.classList.add("has-bg", "bg-image");
    const bust = encodeURIComponent(String(cfg.backgroundVersion || Date.now()));
    el.style.setProperty("--ccs-chat-bg-image", `url("/chat/background?v=${bust}")`);
  } else {
    el.style.removeProperty("--ccs-chat-bg-image");
  }
}

export function updateChat(el: ChatEl, item: unknown, chatConfig: unknown): void {
  const appearance = resolveChatAppearance(item, chatConfig);
  el._showTwitchEvents = appearance.showTwitchEvents !== false;
  el._showEventIcons = appearance.showEventIcons !== false;
  el._showBadges = appearance.showBadges !== false;
  el._showEmotes = appearance.showEmotes !== false;
  el._showTimestamps = !!appearance.showTimestamps;
  el._timestampFormat = appearance.timestampFormat;
  el._hideCommands = !!appearance.hideCommands;
  el._nameDisplay = appearance.nameDisplay;
  el._separator = appearance.separator;
  el._uppercaseNames = !!appearance.uppercaseNames;
  el._showStatusLine = appearance.showStatusLine !== false;
  el._useTwitchUserColor = appearance.useTwitchUserColor !== false;
  el._fallbackUserColor = appearance.fallbackUserColor;
  el._animateNewLines = appearance.animateNewLines !== false;
  el._fadeOut = !!appearance.fadeOut;
  el._fadeAfterMs = appearance.fadeAfterMs;
  el._fadeDurationMs = appearance.fadeDurationMs;
  el._maxLines = appearance.maxLines;
  applyChatAppearance(el, appearance);

  const status = el._lines?.querySelector(".ccs-chat-status") as HTMLElement | null;
  if (status && !el._showStatusLine) {
    status.remove();
  }
}

export function clearChatStatus(el: ChatEl): void {
  const status = el._lines && el._lines.querySelector(".ccs-chat-status");
  if (status) {
    el._lines!.innerHTML = "";
  }
}

export function clearChat(el: ChatEl): void {
  if (el._fadeTimers) {
    for (const timer of el._fadeTimers.values()) window.clearTimeout(timer);
    el._fadeTimers.clear();
  }
  el._seenMessageIds = new Set();
  if (!el._lines) return;
  if (el._showStatusLine !== false) {
    el._lines.innerHTML = `<div class="ccs-chat-line ccs-chat-status">Chat bereit</div>`;
  } else {
    el._lines.innerHTML = "";
  }
}

export function removeChatMessageById(el: ChatEl, messageId: string): boolean {
  const id = String(messageId || "");
  if (!id || !el._lines) return false;
  const line = Array.from(el._lines.querySelectorAll(".ccs-chat-line[data-message-id]")).find(
    (node) => (node as HTMLElement).dataset.messageId === id
  ) as HTMLElement | undefined;
  if (!line) {
    el._seenMessageIds?.delete(id);
    return false;
  }
  line.remove();
  el._seenMessageIds?.delete(id);
  return true;
}

export function removeChatMessagesByUser(el: ChatEl, userLogin: string, userId = ""): number {
  if (!el._lines) return 0;
  const login = String(userLogin || "").toLowerCase();
  const id = String(userId || "");
  let removed = 0;
  const lines = Array.from(el._lines.querySelectorAll(".ccs-chat-line[data-message-id]"));
  for (const line of lines) {
    const node = line as HTMLElement;
    const matchLogin = login && String(node.dataset.userLogin || "").toLowerCase() === login;
    const matchId = id && String(node.dataset.userId || "") === id;
    if (!matchLogin && !matchId) continue;
    if (node.dataset.messageId) el._seenMessageIds?.delete(node.dataset.messageId);
    node.remove();
    removed += 1;
  }
  return removed;
}

export function trimChatLines(el: ChatEl): void {
  const root = el._lines;
  if (!root) return;
  const max = el._maxLines || 80;
  while (root.children.length > max) {
    const first = root.firstChild as HTMLElement | null;
    if (first && first.dataset && first.dataset.messageId && el._seenMessageIds) {
      el._seenMessageIds.delete(first.dataset.messageId);
    }
    if (first) root.removeChild(first);
  }
  root.scrollTop = root.scrollHeight;
}

export function appendChatMessage(el: ChatEl, data: Record<string, unknown>): boolean {
  const messageId = data && data.messageId ? String(data.messageId) : "";
  if (messageId) {
    el._seenMessageIds = el._seenMessageIds || new Set();
    if (el._seenMessageIds.has(messageId)) {
      return false;
    }
  }

  const text = partsPlainText(data.parts);
  if (el._hideCommands && text.trim().startsWith("!")) {
    return false;
  }

  if (messageId) {
    el._seenMessageIds!.add(messageId);
  }

  clearChatStatus(el);
  const line = document.createElement("div");
  line.className = "ccs-chat-line";
  if (messageId) line.dataset.messageId = messageId;
  const userLogin = String(data.userLogin || "");
  const userId = String(data.userId || "");
  if (userLogin) line.dataset.userLogin = userLogin;
  if (userId) line.dataset.userId = userId;

  const color = resolveChatUserColor(data.color, userLogin || data.userName, {
    useTwitchUserColor: el._useTwitchUserColor !== false,
    fallbackUserColor: el._fallbackUserColor
  });
  const name = displayName(data, el._nameDisplay || "display");
  const sep = separatorText(el._separator || "colon");
  const timeHtml =
    el._showTimestamps && data.at
      ? `<span class="ccs-chat-time">${escapeHtml(formatTimestamp(data.at, el._timestampFormat || "hh:mm"))}</span>`
      : "";
  const badgesHtml = el._showBadges === false ? "" : renderChatBadges(data.badges);
  const messageHtml = renderChatParts(data.parts, el._showEmotes !== false);

  line.innerHTML =
    `${timeHtml}` +
    `${badgesHtml}` +
    `<span class="ccs-chat-user" style="color:${color}">${escapeHtml(name)}${escapeHtml(sep)}</span>` +
    `<span class="ccs-chat-message">${messageHtml}</span>`;
  el._lines!.appendChild(line);
  scheduleFade(el, line);
  trimChatLines(el);
  return true;
}

export function appendChatEvent(el: ChatEl, payload: Record<string, unknown>): void {
  clearChatStatus(el);
  const line = document.createElement("div");
  line.className = "ccs-chat-line ccs-chat-event";
  line.dataset.eventType = String(payload.type || "");
  const iconUrl = CHAT_EVENT_ICONS[String(payload.type || "")];
  const icon =
    el._showEventIcons !== false && iconUrl
      ? `<img class="ccs-chat-event-icon" src="${escapeHtml(iconUrl)}" alt="" />`
      : "";
  const summary = escapeHtml(String(payload.summary || payload.type || "Event"));
  line.innerHTML = `${icon}<span class="ccs-chat-event-summary">${summary}</span>`;
  el._lines!.appendChild(line);
  scheduleFade(el, line);
  trimChatLines(el);
}
