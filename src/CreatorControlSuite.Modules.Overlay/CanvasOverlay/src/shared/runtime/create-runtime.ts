import type { CreateRuntime, ItemNode, Layout, LayoutItem, RuntimeOptions } from '../types';
import { DEFAULT_LAYOUT } from '../defaults/layout';
import { WIDGET_DEFAULTS } from '../defaults/widgets';
import { SHAPE_DEFAULTS } from '../defaults/shapes';
import { uid } from '../utils/format';
import { fetchJson } from '../net/fetch-json';
import { createItemContent, applyItemBox } from './item-content';
import { applyItemEffects } from '../effects/apply';
import {
  updateOnline, updateSpotify, updateChat, updateEndingStats, updateText, updateImage,
  updateCountdown, updateSocials, updatePartnerRoulette, paintSpotifyProgress, paintEndingStats, paintCountdown,
  enqueueAlert, appendChatMessage, appendChatEvent, CHAT_EVENT_TYPES
} from './core-functions';

export function createRuntime(options: RuntimeOptions): CreateRuntime {
    const opts = options || {};
    const root = opts.root;
    const editing = !!opts.editing;
    const soloType = opts.soloType || null;
    let layout = opts.layout || { ...DEFAULT_LAYOUT, items: [] };
    let data = opts.data || {};
    let chatConfig = opts.chatConfig || null;
    const itemNodes = new Map();
    let selectedId = null;
    let onSelect = opts.onSelect || null;
    let onChange = opts.onChange || null;
    const chatHistory = [];
    const seenMessageIds = new Set();
    const CHAT_HISTORY_LIMIT = 200;

    const canvas = document.createElement("div");
    canvas.className = "ccs-canvas";
    root.appendChild(canvas);

    function trimChatHistory() {
      while (chatHistory.length > CHAT_HISTORY_LIMIT) {
        const removed = chatHistory.shift();
        if (removed && removed.kind === "message" && removed.data && removed.data.messageId) {
          seenMessageIds.delete(String(removed.data.messageId));
        }
      }
    }

    function rememberChatMessage(messageData) {
      const data = messageData || {};
      const messageId = data.messageId ? String(data.messageId) : "";
      if (messageId && seenMessageIds.has(messageId)) {
        return false;
      }
      if (messageId) {
        seenMessageIds.add(messageId);
      }
      chatHistory.push({ kind: "message", data });
      trimChatHistory();
      return true;
    }

    function rememberChatEvent(payload) {
      chatHistory.push({ kind: "event", payload: payload || {} });
      trimChatHistory();
      return true;
    }

    function restoreChatWidget(el) {
      if (!el || !el._lines) return;
      el._lines.innerHTML = "";
      el._seenMessageIds = new Set();
      for (const entry of chatHistory) {
        if (entry.kind === "message") {
          appendChatMessage(el, entry.data || {});
        } else if (entry.kind === "event" && el._showTwitchEvents !== false) {
          appendChatEvent(el, entry.payload || {});
        }
      }
      if (!el._lines.children.length) {
        el._lines.innerHTML = `<div class="ccs-chat-line ccs-chat-status">Chat bereit</div>`;
      }
    }

    function restoreAllChatWidgets() {
      for (const node of itemNodes.values()) {
        if (node.item.type === "chat") {
          restoreChatWidget(node.content);
        }
      }
    }

    function ingestChatHistory(events) {
      if (!Array.isArray(events)) {
        return;
      }
      let added = false;
      for (const evt of events) {
        if (evt && evt.source === "twitch" && evt.type === "channel.chat.message") {
          if (rememberChatMessage(evt.data || {})) {
            added = true;
          }
        }
      }
      if (added) {
        restoreAllChatWidgets();
      }
    }

    async function loadChatHistory() {
      try {
        const payload = await fetchJson("/chat/history");
        ingestChatHistory(payload && payload.events);
      } catch (_) { /* optional */ }
    }

    function fit() {
      const cw = layout.canvasWidth || 1920;
      const ch = layout.canvasHeight || 1080;
      canvas.style.width = cw + "px";
      canvas.style.height = ch + "px";
      const pad = editing ? 32 : 0;
      const rw = Math.max(1, (root.clientWidth || cw) - pad * 2);
      const rh = Math.max(1, (root.clientHeight || ch) - pad * 2);
      const scale = Math.min(rw / cw, rh / ch);
      canvas.style.transform = `scale(${scale})`;
      if (opts.center) {
        canvas.style.left = (pad + (rw - cw * scale) / 2) + "px";
        canvas.style.top = (pad + (rh - ch * scale) / 2) + "px";
      }
    }

    function clearItems() {
      itemNodes.clear();
      canvas.querySelectorAll(".ccs-item").forEach((node) => node.remove());
    }

    function renderItems() {
      clearItems();
      const items = (layout.items || []).slice().sort((a, b) => (a.z || 0) - (b.z || 0));
      for (const item of items) {
        if (soloType && item.type !== soloType && `${item.kind}/${item.type}` !== soloType) {
          continue;
        }
        const wrapper = document.createElement("div");
        wrapper.className = "ccs-item"
          + (editing ? " edit-chrome" : "")
          + (editing && item.id === selectedId ? " editing" : "")
          + (item.type === "shape.cutout" ? " ccs-item-cutout" : "");
        wrapper.dataset.id = item.id;
        applyItemBox(wrapper, item);
        const content = createItemContent(item);
        content.dataset.role = "content";
        wrapper.appendChild(content);
        applyItemEffects(wrapper, item);
        if (editing) {
          ["nw", "ne", "sw", "se"].forEach((pos) => {
            const h = document.createElement("div");
            h.className = "ccs-handle " + pos;
            h.dataset.handle = pos;
            wrapper.appendChild(h);
          });
        }
        canvas.appendChild(wrapper);
        itemNodes.set(item.id, { wrapper, content, item });
        refreshItemData(item.id);
      }
      restoreAllChatWidgets();
      fit();
    }

    function refreshItemData(id) {
      const node = itemNodes.get(id);
      if (!node) return;
      const item = node.item;
      if (item.type === "online") updateOnline(node.content, item, data);
      if (item.type === "music" || item.type === "spotify") updateSpotify(node.content, item, data);
      if (item.type === "chat") updateChat(node.content, item, chatConfig);
      if (item.type === "ending-stats") updateEndingStats(node.content, item, data);
      if (item.type === "text") updateText(node.content, item);
      if (item.type === "image") updateImage(node.content, item);
      if (item.type === "countdown") updateCountdown(node.content, item, data);
      if (item.type === "socials") updateSocials(node.content, item);
      if (item.type === "partner-roulette") updatePartnerRoulette(node.content, item);
    }

    function refreshAllData() {
      for (const id of itemNodes.keys()) {
        refreshItemData(id);
      }
    }

    function setLayout(next, keepSelection) {
      layout = next || { ...DEFAULT_LAYOUT, items: [] };
      if (!keepSelection) {
        selectedId = null;
        renderItems();
        return;
      }
      renderItems();
      // Layout WS/PUT echoes replace item object refs; rebind selection so editors
      // (and onSelect consumers) hold the live item instead of a stale closure.
      if (keepSelection && selectedId) {
        if (!(layout.items || []).some((i) => i.id === selectedId)) {
          selectedId = null;
        }
        select(selectedId);
      }
    }

    function setData(next) {
      data = next || {};
      refreshAllData();
    }

    function setChatConfig(next) {
      chatConfig = next || null;
      for (const node of itemNodes.values()) {
        if (node.item.type === "chat") {
          updateChat(node.content, node.item, chatConfig);
        }
      }
    }

    function select(id) {
      selectedId = id;
      for (const [key, node] of itemNodes) {
        node.wrapper.classList.toggle("editing", editing && key === id);
      }
      if (onSelect) {
        const item = (layout.items || []).find((i) => i.id === id) || null;
        onSelect(item);
      }
    }

    function emitChange() {
      if (onChange) onChange(layout);
    }

    function handleRealtime(evt) {
      if (!evt || !evt.type) return;
      if (evt.type === "app.overlay.layout") {
        const instanceId = (evt.data && evt.data.instanceId) || "";
        if (opts.instanceId && instanceId && instanceId !== opts.instanceId) {
          return;
        }
        try {
          const parsed = JSON.parse(evt.data.layout || "{}");
          setLayout(parsed, true);
        } catch (_) { /* ignore */ }
        return;
      }
      if (evt.type === "app.alert" || (evt.source === "twitch" && /follow|subscribe|cheer|raid/i.test(evt.type || ""))) {
        for (const node of itemNodes.values()) {
          if (node.item.type !== "alert") continue;
          enqueueAlert(node.content, node.item, {
            alertType: (evt.data && (evt.data.alertType || evt.type)) || evt.type,
            user: (evt.data && (evt.data.user || evt.data.user_name || evt.data.userName)) || "",
            summary: evt.summary || ""
          });
        }
      }
      if (evt.source === "twitch" && evt.type === "channel.chat.message") {
        const messageData = evt.data || {};
        if (!rememberChatMessage(messageData)) {
          return;
        }
        for (const node of itemNodes.values()) {
          if (node.item.type !== "chat") continue;
          appendChatMessage(node.content, messageData);
        }
        return;
      }
      if (evt.source === "twitch" && CHAT_EVENT_TYPES.has(evt.type)) {
        rememberChatEvent(evt);
        for (const node of itemNodes.values()) {
          if (node.item.type !== "chat") continue;
          if (node.content._showTwitchEvents === false) continue;
          appendChatEvent(node.content, evt);
        }
      }
      if (evt.type === "app.stream.live" || evt.type === "app.music.track" || evt.type === "app.spotify.track") {
        // full refresh comes from data poll; light hint only
        refreshAllData();
      }
      if (evt.type === "app.countdown") {
        const payload = evt.data || {};
        data = data || {};
        data.countdown = Object.assign({}, data.countdown || {}, {
          isRunning: payload.isRunning === true || payload.isRunning === "true",
          remainingSeconds: Number(payload.remainingSeconds) || 0,
          totalSeconds: Number(payload.totalSeconds) || 0,
          label: payload.label || (data.countdown && data.countdown.label) || "Countdown",
          endsAt: payload.endsAt || null
        });
        for (const node of itemNodes.values()) {
          if (node.item.type === "countdown") {
            paintCountdown(node.content, node.item, data);
          }
        }
      }
    }

    function tick() {
      for (const node of itemNodes.values()) {
        if (node.item.type === "online") updateOnline(node.content, node.item, data);
        if (node.item.type === "music" || node.item.type === "spotify") paintSpotifyProgress(node.content);
        if (node.item.type === "ending-stats") paintEndingStats(node.content, data);
        if (node.item.type === "countdown") paintCountdown(node.content, node.item, data);
      }
    }

    setInterval(tick, 250);
    window.addEventListener("resize", fit);
    renderItems();

    return {
      canvas,
      fit,
      setLayout,
      getLayout: () => layout,
      setData,
      getData: () => data,
      setChatConfig,
      loadChatHistory,
      ingestChatHistory,
      select,
      getSelectedId: () => selectedId,
      handleRealtime,
      renderItems,
      emitChange,
      itemNodes,
      defaultsFor(type, kind) {
        if (kind === "shape" || SHAPE_DEFAULTS[type]) {
          return SHAPE_DEFAULTS[type] || { w: 300, h: 300, props: { color: "#ff7a00" } };
        }
        return WIDGET_DEFAULTS[type] || { w: 240, h: 120, props: {} };
      },
      createItem(type, kind, x, y) {
        const def = this.defaultsFor(type, kind);
        return {
          id: uid(),
          kind: kind || (SHAPE_DEFAULTS[type] ? "shape" : "widget"),
          type,
          x: x || 80,
          y: y || 80,
          w: def.w,
          h: def.h,
          z: (layout.items || []).length + 1,
          rotation: 0,
          locked: false,
          effects: [],
          props: { ...(def.props || {}) }
        };
      },
      WIDGET_DEFAULTS,
      SHAPE_DEFAULTS,
      DEFAULT_LAYOUT
    };
  }
