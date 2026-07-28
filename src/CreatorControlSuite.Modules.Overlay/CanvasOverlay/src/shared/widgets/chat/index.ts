import { escapeHtml } from '../../utils/html';

export { escapeHtml } from '../../utils/html';

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

  const CHAT_EVENT_ICONS = {
    "channel.follow": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.subscribe": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.subscription.message": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.subscription.gift": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.cheer": "https://static-cdn.jtvnw.net/badges/v1/73b5c3fb-7f24-432c-a4ae-c6c3d5e3d5c7/2",
    "channel.raid": "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85b62/2",
    "stream.online": "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/2",
    "stream.offline": "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/2"
  };

export function renderChatParts(partsJson) {
    let parts = [];
    try {
      parts = JSON.parse(partsJson || "[]");
    } catch {
      return "";
    }

    return parts.map((part) => {
      if (part.type === "emote" && part.url) {
        const alt = escapeHtml(part.text || "");
        return `<img class="ccs-chat-emote" src="${escapeHtml(part.url)}" alt="${alt}" title="${alt}" />`;
      }
      return escapeHtml(part.text || "");
    }).join("");
  }

export function renderChatBadges(badgesJson) {
    let badges = [];
    try {
      const parsed = JSON.parse(badgesJson || "[]");
      badges = Array.isArray(parsed) ? parsed : [];
    } catch {
      return "";
    }

    return badges
      .filter((badge) => badge && badge.url)
      .map((badge) => {
        const title = escapeHtml(badge.title || badge.setId || "");
        return `<img class="ccs-chat-badge" src="${escapeHtml(badge.url)}" alt="" title="${title}" />`;
      })
      .join("");
  }

export function createChatEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-chat";
    el.innerHTML =
      `<div class="ccs-chat-bg"></div>` +
      `<div class="ccs-chat-lines"><div class="ccs-chat-line ccs-chat-status">Chat bereit</div></div>`;
    el._lines = el.querySelector(".ccs-chat-lines");
    el._seenMessageIds = new Set();
    updateChat(el, item, null);
    return el;
  }

export function resolveChatAppearance(item, chatConfig) {
    const cfg = chatConfig || {};
    const props = item && item.props ? item.props : {};
    let opacity;
    if (props.backgroundOpacityPercent != null && props.backgroundOpacityPercent !== "") {
      opacity = Number(props.backgroundOpacityPercent) / 100;
    } else if (props.backgroundOpacity != null && props.backgroundOpacity !== "") {
      opacity = Number(props.backgroundOpacity);
    } else if (cfg.backgroundOpacity != null) {
      opacity = Number(cfg.backgroundOpacity);
    } else {
      opacity = 0.55;
    }

    return {
      showTwitchEvents: props.showTwitchEvents !== undefined
        ? props.showTwitchEvents !== false
        : cfg.showTwitchEvents !== false,
      maxLines: Math.max(1, Number(props.maxLines != null ? props.maxLines : 80) || 80),
      backgroundType: String(props.backgroundType || cfg.backgroundType || "None"),
      backgroundColor: String(props.backgroundColor || cfg.backgroundColor || "#000000"),
      backgroundOpacity: Math.min(1, Math.max(0, opacity)),
      paddingPx: Math.max(0, Number(props.paddingPx != null ? props.paddingPx : (cfg.paddingPx ?? 12)) || 0),
      borderRadiusPx: Math.max(0, Number(props.borderRadiusPx != null ? props.borderRadiusPx : (cfg.borderRadiusPx ?? 12)) || 0),
      gapPx: Math.max(0, Number(props.gapPx != null ? props.gapPx : (cfg.gapPx ?? 6)) || 0),
      fontSizePx: Math.min(72, Math.max(8, Number(props.fontSizePx != null ? props.fontSizePx : (cfg.fontSizePx ?? 18)) || 18)),
      fontFamily: String(props.fontFamily || cfg.fontFamily || "Segoe UI, system-ui, sans-serif").trim()
        || "Segoe UI, system-ui, sans-serif",
      backgroundVersion: cfg.backgroundVersion || "0"
    };
  }

export function applyChatAppearance(el, appearance) {
    const cfg = appearance || {};
    const type = String(cfg.backgroundType || "None");
    const opacity = Math.min(1, Math.max(0, Number(cfg.backgroundOpacity ?? 0.55)));
    const padding = Math.max(0, Number(cfg.paddingPx ?? 12));
    const radius = Math.max(0, Number(cfg.borderRadiusPx ?? 12));
    const gap = Math.max(0, Number(cfg.gapPx ?? 6));
    const color = String(cfg.backgroundColor || "#000000");
    const fontSize = Math.min(72, Math.max(8, Number(cfg.fontSizePx ?? 18) || 18));
    const fontFamily = String(cfg.fontFamily || "Segoe UI, system-ui, sans-serif");

    el.style.setProperty("--ccs-chat-padding", `${padding}px`);
    el.style.setProperty("--ccs-chat-radius", `${radius}px`);
    el.style.setProperty("--ccs-chat-gap", `${gap}px`);
    el.style.setProperty("--ccs-chat-bg-opacity", String(opacity));
    el.style.setProperty("--ccs-chat-bg-color", color);
    el.style.setProperty("--ccs-chat-font-size", `${fontSize}px`);
    el.style.setProperty("--ccs-chat-font-family", fontFamily);
    el.style.setProperty("--ccs-chat-emote-size", `${Math.round(fontSize * 1.55)}px`);
    el.style.setProperty("--ccs-chat-badge-size", `${Math.round(fontSize)}px`);

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

export function updateChat(el, item, chatConfig) {
    const appearance = resolveChatAppearance(item, chatConfig);
    el._showTwitchEvents = appearance.showTwitchEvents !== false;
    el._maxLines = appearance.maxLines;
    applyChatAppearance(el, appearance);
  }

export function clearChatStatus(el) {
    const status = el._lines && el._lines.querySelector(".ccs-chat-status");
    if (status) {
      el._lines.innerHTML = "";
    }
  }

export function trimChatLines(el) {
    const root = el._lines;
    if (!root) return;
    const max = el._maxLines || 80;
    while (root.children.length > max) {
      const first = root.firstChild;
      if (first && first.dataset && first.dataset.messageId && el._seenMessageIds) {
        el._seenMessageIds.delete(first.dataset.messageId);
      }
      root.removeChild(first);
    }
    root.scrollTop = root.scrollHeight;
  }

export function appendChatMessage(el, data) {
    const messageId = data && data.messageId ? String(data.messageId) : "";
    if (messageId) {
      el._seenMessageIds = el._seenMessageIds || new Set();
      if (el._seenMessageIds.has(messageId)) {
        return false;
      }
      el._seenMessageIds.add(messageId);
    }

    clearChatStatus(el);
    const line = document.createElement("div");
    line.className = "ccs-chat-line";
    if (messageId) {
      line.dataset.messageId = messageId;
    }
    const color = data.color && /^#[0-9a-fA-F]{6}$/.test(data.color)
      ? data.color
      : "#dedede";
    line.innerHTML =
      `${renderChatBadges(data.badges)}` +
      `<span class="ccs-chat-user" style="color:${color}">${escapeHtml(data.userName || data.userLogin || "user")}:</span>` +
      `<span class="ccs-chat-message">${renderChatParts(data.parts)}</span>`;
    el._lines.appendChild(line);
    trimChatLines(el);
    return true;
  }

export function appendChatEvent(el, payload) {
    clearChatStatus(el);
    const line = document.createElement("div");
    line.className = "ccs-chat-line ccs-chat-event";
    line.dataset.eventType = payload.type || "";
    const iconUrl = CHAT_EVENT_ICONS[payload.type];
    const icon = iconUrl
      ? `<img class="ccs-chat-event-icon" src="${escapeHtml(iconUrl)}" alt="" />`
      : "";
    const summary = escapeHtml(payload.summary || payload.type || "Event");
    line.innerHTML = `${icon}<span class="ccs-chat-event-summary">${summary}</span>`;
    el._lines.appendChild(line);
    trimChatLines(el);
  }
