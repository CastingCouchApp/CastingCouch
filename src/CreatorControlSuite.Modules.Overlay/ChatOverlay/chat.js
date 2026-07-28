(() => {
  const root = document.getElementById("chat");
  const maxLines = 80;
  const seenMessageIds = new Set();
  const eventTypes = new Set([
    "channel.follow",
    "channel.subscribe",
    "channel.subscription.message",
    "channel.subscription.gift",
    "channel.cheer",
    "channel.raid",
    "stream.online",
    "stream.offline"
  ]);

  // Twitch-Badge-Icons als Event-Marker (kein Text-Label).
  const eventIcons = {
    "channel.follow": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.subscribe": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.subscription.message": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.subscription.gift": "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
    "channel.cheer": "https://static-cdn.jtvnw.net/badges/v1/73b5c3fb-7f24-432c-a4ae-c6c3d5e3d5c7/2",
    "channel.raid": "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85b62/2",
    "stream.online": "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/2",
    "stream.offline": "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/2"
  };

  let showTwitchEvents = true;
  const panel = document.getElementById("panel");
  const TWITCH_DEFAULT_COLORS = [
    "#FF0000", "#0000FF", "#00FF00", "#B22222", "#FF7F50",
    "#9ACD32", "#FF4500", "#2E8B57", "#DAA520", "#D2691E",
    "#5F9EA0", "#1E90FF", "#FF69B4", "#8A2BE2", "#00FF7F"
  ];

  function resolveChatUserColor(color, login) {
    const raw = String(color ?? "").trim();
    const match = /^#?([0-9a-fA-F]{6})$/.exec(raw);
    if (match) {
      return `#${match[1].toUpperCase()}`;
    }
    const name = String(login || "user").trim() || "user";
    const n = name.charCodeAt(0) + name.charCodeAt(name.length - 1);
    return TWITCH_DEFAULT_COLORS[n % TWITCH_DEFAULT_COLORS.length];
  }

  function wsUrl() {
    const protocol = location.protocol === "https:" ? "wss:" : "ws:";
    return `${protocol}//${location.host}/ws`;
  }

  function applyAppearance(config) {
    if (!panel) {
      return;
    }

    const type = String(config.backgroundType || "None");
    const opacity = Math.min(1, Math.max(0, Number(config.backgroundOpacity ?? 0.55)));
    const padding = Math.max(0, Number(config.paddingPx ?? 12));
    const radius = Math.max(0, Number(config.borderRadiusPx ?? 12));
    const gap = Math.max(0, Number(config.gapPx ?? 6));
    const color = String(config.backgroundColor || "#000000");
    const fontSize = Math.min(72, Math.max(8, Number(config.fontSizePx ?? 18) || 18));
    const fontFamily = String(config.fontFamily || "Segoe UI, system-ui, sans-serif").trim()
      || "Segoe UI, system-ui, sans-serif";

    panel.style.setProperty("--chat-padding", `${padding}px`);
    panel.style.setProperty("--chat-radius", `${radius}px`);
    panel.style.setProperty("--chat-gap", `${gap}px`);
    panel.style.setProperty("--chat-bg-opacity", String(opacity));
    panel.style.setProperty("--chat-bg-color", color);
    panel.style.setProperty("--chat-font-size", `${fontSize}px`);
    panel.style.setProperty("--chat-font", fontFamily);
    panel.style.setProperty("--chat-emote-size", `${Math.round(fontSize * 1.55)}px`);
    panel.style.setProperty("--chat-badge-size", `${Math.round(fontSize)}px`);
    document.documentElement.style.setProperty("--chat-font", fontFamily);
    document.documentElement.style.setProperty("--chat-font-size", `${fontSize}px`);

    panel.classList.remove("has-bg", "bg-image");
    if (type === "Color") {
      panel.classList.add("has-bg");
    } else if (type === "Image") {
      panel.classList.add("has-bg", "bg-image");
      const bust = encodeURIComponent(String(config.backgroundVersion || Date.now()));
      panel.style.setProperty("--chat-bg-image", `url("/chat/background?v=${bust}")`);
    } else {
      panel.style.removeProperty("--chat-bg-image");
    }
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function renderParts(partsJson) {
    let parts = [];
    try {
      parts = JSON.parse(partsJson || "[]");
    } catch {
      return "";
    }

    return parts.map((part) => {
      if (part.type === "emote" && part.url) {
        const alt = escapeHtml(part.text || "");
        return `<img class="emote" src="${escapeHtml(part.url)}" alt="${alt}" title="${alt}" />`;
      }
      return escapeHtml(part.text || "");
    }).join("");
  }

  function renderBadges(badgesJson) {
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
        return `<img class="badge-icon" src="${escapeHtml(badge.url)}" alt="" title="${title}" />`;
      })
      .join("");
  }

  function clearStatusIfNeeded() {
    const status = root.querySelector(".status");
    if (status) {
      root.innerHTML = "";
    }
  }

  function clearChat() {
    seenMessageIds.clear();
    setStatus("Chat bereit");
  }

  function removeMessageById(messageId) {
    const id = String(messageId || "");
    if (!id) return;
    seenMessageIds.delete(id);
    const lines = Array.from(root.querySelectorAll(".line[data-message-id]"));
    for (const line of lines) {
      if (line.dataset.messageId === id) {
        line.remove();
      }
    }
  }

  function removeMessagesByUser(userLogin, userId) {
    const login = String(userLogin || "").toLowerCase();
    const id = String(userId || "");
    const lines = Array.from(root.querySelectorAll(".line[data-message-id]"));
    for (const line of lines) {
      const matchLogin = login && String(line.dataset.userLogin || "").toLowerCase() === login;
      const matchId = id && String(line.dataset.userId || "") === id;
      if (!matchLogin && !matchId) continue;
      if (line.dataset.messageId) seenMessageIds.delete(line.dataset.messageId);
      line.remove();
    }
  }

  function trimLines() {
    while (root.children.length > maxLines) {
      const first = root.firstChild;
      if (first && first.dataset && first.dataset.messageId) {
        seenMessageIds.delete(first.dataset.messageId);
      }
      root.removeChild(first);
    }
    root.scrollTop = root.scrollHeight;
  }

  function appendMessage(data) {
    const messageId = data && data.messageId ? String(data.messageId) : "";
    if (messageId && seenMessageIds.has(messageId)) {
      return false;
    }
    if (messageId) {
      seenMessageIds.add(messageId);
    }

    clearStatusIfNeeded();
    const line = document.createElement("div");
    line.className = "line";
    if (messageId) {
      line.dataset.messageId = messageId;
    }
    const userLogin = String(data.userLogin || "");
    const userId = String(data.userId || "");
    if (userLogin) line.dataset.userLogin = userLogin;
    if (userId) line.dataset.userId = userId;

    const color = resolveChatUserColor(data.color, userLogin || data.userName);

    line.innerHTML =
      `${renderBadges(data.badges)}` +
      `<span class="user" style="color:${color}">${escapeHtml(data.userName || data.userLogin || "user")}:</span>` +
      `<span class="message">${renderParts(data.parts)}</span>`;

    root.appendChild(line);
    trimLines();
    return true;
  }

  function appendEvent(payload) {
    clearStatusIfNeeded();
    const line = document.createElement("div");
    line.className = "line event";
    line.dataset.eventType = payload.type || "";

    const iconUrl = eventIcons[payload.type];
    const icon = iconUrl
      ? `<img class="event-icon" src="${escapeHtml(iconUrl)}" alt="" />`
      : "";
    const summary = escapeHtml(payload.summary || payload.type || "Event");
    line.innerHTML = `${icon}<span class="event-summary">${summary}</span>`;

    root.appendChild(line);
    trimLines();
  }

  function setStatus(text) {
    root.innerHTML = `<div class="line status">${escapeHtml(text)}</div>`;
  }

  async function loadConfig() {
    try {
      const response = await fetch("/chat/config", { cache: "no-store" });
      if (!response.ok) {
        return;
      }
      const config = await response.json();
      showTwitchEvents = config.showTwitchEvents !== false;
      applyAppearance(config);
    } catch {
      // defaults
    }
  }

  async function loadHistory() {
    try {
      const response = await fetch("/chat/history", { cache: "no-store" });
      if (!response.ok) {
        return;
      }
      const payload = await response.json();
      const events = Array.isArray(payload.events) ? payload.events : [];
      for (const evt of events) {
        if (evt?.source === "twitch" && evt?.type === "channel.chat.message") {
          appendMessage(evt.data || {});
        }
      }
    } catch {
      // optional
    }
  }

  function connect() {
    const socket = new WebSocket(wsUrl());

    socket.addEventListener("open", () => {
      void loadConfig().then(loadHistory);
    });

    socket.addEventListener("message", (event) => {
      let payload;
      try {
        payload = JSON.parse(event.data);
      } catch {
        return;
      }

      if (
        (payload?.source === "app" && payload?.type === "app.chat.clear") ||
        (payload?.source === "twitch" && payload?.type === "channel.chat.clear")
      ) {
        clearChat();
        return;
      }

      if (payload?.source === "twitch" && payload?.type === "channel.chat.message_delete") {
        removeMessageById((payload.data && (payload.data.messageId || payload.data.message_id)) || "");
        return;
      }

      if (payload?.source === "twitch" && payload?.type === "channel.chat.clear_user_messages") {
        removeMessagesByUser(
          (payload.data && (payload.data.targetUserLogin || payload.data.target_user_login)) || "",
          (payload.data && (payload.data.targetUserId || payload.data.target_user_id)) || ""
        );
        return;
      }

      if (payload?.source === "twitch" && payload?.type === "channel.chat.message") {
        appendMessage(payload.data || {});
        return;
      }

      if (
        showTwitchEvents &&
        payload?.source === "twitch" &&
        eventTypes.has(payload?.type)
      ) {
        appendEvent(payload);
      }
    });

    socket.addEventListener("close", () => {
      setTimeout(connect, 1500);
    });

    socket.addEventListener("error", () => {
      try { socket.close(); } catch { /* ignore */ }
    });
  }

  setStatus("Verbinde…");
  connect();
})();
