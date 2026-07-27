import type { LayoutItem } from '../types';
import { prop } from '../utils/prop';
import { escapeHtml } from '../utils/html';
import { formatClock, formatUptime, formatMs } from '../utils/format';
import { rgbaFrom } from '../utils/color';
import { SCENE_BG_PRESETS } from '../defaults/scene-bg';
import { CARD_FRAME_VARIANTS, SHAPE_DEFAULTS } from '../defaults/shapes';
export function createOnlineEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-online";
    el.innerHTML =
      `<div class="ccs-online-row"><span class="ccs-online-dot"></span>` +
      `<span class="ccs-online-label">OFFLINE</span></div>` +
      `<div class="ccs-online-time"></div>`;
    return el;
  }

export function updateOnline(el, item, data) {
    const stream = (data && data.stream) || {};
    const live = stream.isLive === true;
    el.classList.toggle("is-live", live);
    el.querySelector(".ccs-online-label").textContent = live ? "ONLINE" : "OFFLINE";
    const parts = [];
    if (prop(item, "showClock", true)) {
      parts.push(formatClock(new Date()));
    }
    if (prop(item, "showUptime", true) && live) {
      parts.push("Live " + formatUptime(stream.elapsedSeconds));
    }
    el.querySelector(".ccs-online-time").textContent = parts.join(" Â· ");
  }

export function createAlertEl() {
    const el = document.createElement("div");
    el.className = "ccs-alert";
    el.innerHTML =
      `<div class="ccs-alert-card">` +
      `<div class="ccs-alert-type"></div>` +
      `<div class="ccs-alert-user"></div>` +
      `<div class="ccs-alert-summary"></div>` +
      `</div>`;
    el._queue = [];
    el._showing = false;
    return el;
  }

export const ENDING_STATS_VARIANTS = [
    "classic", "neon", "minimal", "cards", "strip",
    "bold", "outline", "solid", "gradient", "compact"
  ];

export function endingStatsVariant(item) {
    const raw = String(prop(item, "variant", "classic") || "classic").toLowerCase();
    return ENDING_STATS_VARIANTS.includes(raw) ? raw : "classic";
  }

export function createEndingStatsEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-ending-stats";
    el.innerHTML =
      `<div class="ccs-ending-stats-title">STREAM-STATISTIK</div>` +
      `<div class="ccs-ending-stats-grid">` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="time">00:00:00</div><span class="ccs-ending-stat-label">Livezeit</span></div>` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="gain">+0</div><span class="ccs-ending-stat-label">Neue Follower</span></div>` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="peak">0</div><span class="ccs-ending-stat-label">Peak Zuschauer</span></div>` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="avg">0</div><span class="ccs-ending-stat-label">Ã˜ Zuschauer</span></div>` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="subs">0</div><span class="ccs-ending-stat-label">Subs gesamt</span></div>` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="chat">0</div><span class="ccs-ending-stat-label">Chat</span></div>` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="alarms">0</div><span class="ccs-ending-stat-label">Alarme</span></div>` +
      `<div class="ccs-ending-stat"><div class="ccs-ending-stat-value" data-stat="goal">0 / 200</div><span class="ccs-ending-stat-label">Followerziel</span></div>` +
      `</div>`;
    applyEndingStatsVariant(el, item);
    if (typeof ResizeObserver !== "undefined") {
      el._ro = new ResizeObserver(() => fitEndingStats(el));
      el._ro.observe(el);
    }
    requestAnimationFrame(() => fitEndingStats(el));
    return el;
  }

export function applyEndingStatsVariant(el, item) {
    const variant = endingStatsVariant(item);
    ENDING_STATS_VARIANTS.forEach((name) => {
      el.classList.remove("ccs-ending-stats-v-" + name);
    });
    el.classList.add("ccs-ending-stats-v-" + variant);
    el.dataset.variant = variant;
    el.classList.toggle("hide-title", prop(item, "showTitle", true) === false);
  }

export function fitEndingStats(el) {
    if (!el) return;
    const w = Math.max(1, el.clientWidth || el.offsetWidth || 980);
    const h = Math.max(1, el.clientHeight || el.offsetHeight || 220);
    const scale = Math.max(0.45, Math.min(1.35, Math.min(w / 980, h / 220)));
    el.style.setProperty("--ccs-stats-scale", String(scale));
    el.style.setProperty("--ccs-stats-w", w + "px");
    el.style.setProperty("--ccs-stats-h", h + "px");
    let cols = 4;
    if (w < 420) cols = 2;
    else if (w < 720) cols = 3;
    if (h < 140 && w >= 900) cols = 8;
    el.style.setProperty("--ccs-stats-cols", String(cols));
  }

export function updateEndingStats(el, item, data) {
    applyEndingStatsVariant(el, item);
    fitEndingStats(el);
    paintEndingStats(el, data);
  }

export function paintEndingStats(el, data) {
    const stats = (data && data.stats) || {};
    const stream = (data && data.stream) || {};
    const twitch = (data && data.twitch) || {};

    const seconds = Number(
      stats.streamTimeSeconds != null ? stats.streamTimeSeconds : stream.elapsedSeconds
    ) || 0;
    const set = (key, text) => {
      const node = el.querySelector(`[data-stat="${key}"]`);
      if (node) node.textContent = text;
    };

    set("time", formatUptime(seconds));
    set("gain", `+${Number(stats.followersGained || 0)}`);
    set("peak", String(Number(stats.peakViewers || 0)));
    const avg = Number(stats.averageViewers || 0);
    set("avg", Number.isInteger(avg) ? String(avg) : avg.toFixed(1));
    const subs =
      Number(stats.newSubscriptions || 0) + Number(stats.giftSubscriptions || 0);
    set("subs", String(subs));
    set("chat", String(Number(stats.chatMessages || 0)));
    set("alarms", String(Number(stats.alertsPlayed || 0)));
    const followers = Number(twitch.followers || 0);
    const goal = Number(twitch.followerGoal != null ? twitch.followerGoal : 200);
    set("goal", `${followers} / ${goal}`);
  }

  // Brand SVG paths (Simple Iconsâ€“style, viewBox 0 0 24 24). customIconUrl overrides per slot.
export const SOCIALS_PLATFORMS = [
    {
      id: "twitch",
      label: "Twitch",
      showKey: "showTwitch",
      handleKey: "twitchHandle",
      urlKey: "twitchUrl",
      iconUrlKey: "twitchIconUrl",
      fa: "fa-brands fa-twitch",
      color: "#9146FF",
      urlFromHandle: (h) => "https://twitch.tv/" + encodeURIComponent(h),
      svg: "M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z"
    },
    {
      id: "youtube",
      label: "YouTube",
      showKey: "showYoutube",
      handleKey: "youtubeHandle",
      urlKey: "youtubeUrl",
      iconUrlKey: "youtubeIconUrl",
      fa: "fa-brands fa-youtube",
      color: "#FF0000",
      urlFromHandle: (h) => "https://youtube.com/@" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z"
    },
    {
      id: "discord",
      label: "Discord",
      showKey: "showDiscord",
      handleKey: "discordHandle",
      urlKey: "discordUrl",
      iconUrlKey: "discordIconUrl",
      fa: "fa-brands fa-discord",
      color: "#5865F2",
      urlFromHandle: (h) => (String(h).indexOf("http") === 0 ? h : "https://discord.gg/" + encodeURIComponent(h)),
      svg: "M20.317 4.3698a19.7913 19.7913 0 0 0-4.8851-1.5152.0741.0741 0 0 0-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 0 0-.0785-.037 19.7363 19.7363 0 0 0-4.8852 1.515.0699.0699 0 0 0-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 0 0 .0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 0 0 .0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 0 0-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 0 1-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 0 1 .0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 0 1 .0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 0 1-.0066.1276 12.2986 12.2986 0 0 1-1.873.8914.0766.0766 0 0 0-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 0 0 .0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 0 0 .0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 0 0-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z"
    },
    {
      id: "instagram",
      label: "Instagram",
      showKey: "showInstagram",
      handleKey: "instagramHandle",
      urlKey: "instagramUrl",
      iconUrlKey: "instagramIconUrl",
      fa: "fa-brands fa-instagram",
      color: "#E4405F",
      urlFromHandle: (h) => "https://instagram.com/" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M12 0C8.74 0 8.333.015 7.053.072 5.775.132 4.905.333 4.14.63c-.789.306-1.459.717-2.126 1.384S.935 3.35.63 4.14C.333 4.905.131 5.775.072 7.053.012 8.333 0 8.74 0 12s.015 3.667.072 4.947c.06 1.277.261 2.148.558 2.913.306.788.717 1.459 1.384 2.126.667.666 1.336 1.079 2.126 1.384.766.296 1.636.499 2.913.558C8.333 23.988 8.74 24 12 24s3.667-.015 4.947-.072c1.277-.06 2.148-.262 2.913-.558.788-.306 1.459-.718 2.126-1.384.666-.667 1.079-1.335 1.384-2.126.296-.765.499-1.636.558-2.913.06-1.28.072-1.687.072-4.947s-.015-3.667-.072-4.947c-.06-1.277-.262-2.149-.558-2.913-.306-.789-.718-1.459-1.384-2.126C21.319 1.347 20.651.935 19.86.63c-.765-.297-1.636-.499-2.913-.558C15.667.012 15.26 0 12 0zm0 2.16c3.203 0 3.585.016 4.85.071 1.17.055 1.805.249 2.227.415.562.217.96.477 1.382.896.419.42.679.819.896 1.381.164.422.36 1.057.413 2.227.055 1.265.07 1.647.07 4.85s-.015 3.585-.074 4.85c-.061 1.17-.256 1.805-.421 2.227-.224.562-.479.96-.899 1.382-.419.419-.824.679-1.38.896-.42.164-1.065.36-2.235.413-1.274.055-1.645.07-4.859.07-3.211 0-3.586-.015-4.859-.074-1.171-.061-1.816-.256-2.236-.421-.569-.224-.96-.479-1.379-.899-.421-.419-.69-.824-.9-1.38-.165-.42-.359-1.065-.42-2.235-.045-1.26-.061-1.649-.061-4.844 0-3.196.016-3.586.061-4.861.061-1.17.255-1.814.42-2.234.21-.57.479-.96.9-1.381.419-.419.81-.689 1.379-.898.42-.166 1.051-.361 2.221-.421 1.275-.045 1.65-.06 4.859-.06l.045.03zm0 3.678c-3.405 0-6.162 2.76-6.162 6.162 0 3.405 2.76 6.162 6.162 6.162 3.401 0 6.162-2.76 6.162-6.162 0-3.401-2.76-6.162-6.162-6.162zM12 16c-2.21 0-4-1.79-4-4s1.79-4 4-4 4 1.79 4 4-1.79 4-4 4zm7.846-10.405c0 .795-.646 1.44-1.44 1.44-.795 0-1.44-.645-1.44-1.44 0-.794.645-1.439 1.44-1.439.793-.001 1.44.645 1.44 1.439z"
    },
    {
      id: "tiktok",
      label: "TikTok",
      showKey: "showTiktok",
      handleKey: "tiktokHandle",
      urlKey: "tiktokUrl",
      iconUrlKey: "tiktokIconUrl",
      fa: "fa-brands fa-tiktok",
      color: "#69C9D0",
      urlFromHandle: (h) => "https://tiktok.com/@" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M12.525.02c1.31-.02 2.61-.01 3.91-.02.08 1.53.63 3.09 1.75 4.17 1.12 1.11 2.37.69 3.18 1.68v2.56a8.4 8.4 0 0 1-4.15-1.16v7.53a8.15 8.15 0 0 1-8.2 8.2 8.26 8.26 0 0 1-8.2-8.2c0-4.55 3.71-8.2 8.2-8.2.13 0 .26.02.39.03v2.66a5.35 5.35 0 0 0-.39-.03 5.52 5.52 0 0 0-5.52 5.52 5.52 5.52 0 0 0 5.52 5.52 5.54 5.54 0 0 0 5.52-5.52V.02h.01z"
    },
    {
      id: "x",
      label: "X",
      showKey: "showX",
      handleKey: "xHandle",
      urlKey: "xUrl",
      iconUrlKey: "xIconUrl",
      fa: "fa-brands fa-x-twitter",
      color: "#ffffff",
      urlFromHandle: (h) => "https://x.com/" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M18.901 1.153h3.68l-8.04 9.19L24 22.846h-7.406l-5.8-7.584-6.638 7.584H.474l8.6-9.83L0 1.154h7.594l5.243 6.932ZM17.61 20.644h2.039L6.486 3.24H4.298Z"
    },
    {
      id: "kick",
      label: "Kick",
      showKey: "showKick",
      handleKey: "kickHandle",
      urlKey: "kickUrl",
      iconUrlKey: "kickIconUrl",
      fa: "fa-brands fa-kickstarter-k",
      color: "#53FC18",
      urlFromHandle: (h) => "https://kick.com/" + encodeURIComponent(h),
      svg: "M14.563 0.443l-3.485 3.485 3.485 3.485v3.443l-6.928-6.928L14.563 0v0.443zm0 23.114l-3.485-3.485 3.485-3.485v-3.443l-6.928 6.928L14.563 24v-0.443zM9.437 12L1.5 4.063V0.62L12.817 11.937 1.5 23.38v-3.443L9.437 12z"
    },
    {
      id: "bluesky",
      label: "Bluesky",
      showKey: "showBluesky",
      handleKey: "blueskyHandle",
      urlKey: "blueskyUrl",
      iconUrlKey: "blueskyIconUrl",
      fa: "fa-brands fa-bluesky",
      color: "#1185FE",
      urlFromHandle: (h) => "https://bsky.app/profile/" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M12 10.8c-1.087-2.114-4.046-6.053-6.798-7.995C2.566.944 1.561 1.266.902 1.565.139 1.908 0 3.08 0 3.768c0 .69.378 5.65.624 6.479.815 2.736 3.713 3.66 6.383 3.364.136-.02.275-.039.415-.056-.138.022-.276.04-.415.056-3.912.58-7.389 2.004-2.83 7.078 5.013 5.19 6.87-1.113 7.823-4.308.953 3.195 2.05 9.271 7.733 4.308 4.267-4.308 1.172-6.498-2.74-7.078a8.741 8.741 0 0 1-.415-.056c.14.017.279.036.415.056 2.67.297 5.568-.628 6.383-3.364.246-.828.624-5.79.624-6.478 0-.69-.139-1.861-.902-2.206-.659-.298-1.664-.62-4.3 1.24C16.046 4.748 13.087 8.687 12 10.8Z"
    },
    {
      id: "custom1",
      label: "Custom 1",
      showKey: "showCustom1",
      handleKey: "custom1Handle",
      urlKey: "custom1Url",
      iconUrlKey: "custom1IconUrl",
      labelKey: "custom1Label",
      fa: "fa-solid fa-link",
      color: "#ff7a00",
      svg: "M10.59 13.41a1 1 0 0 1 0-1.41l4.24-4.24a1 1 0 1 1 1.41 1.41l-4.24 4.24a1 1 0 0 1-1.41 0zm-2.12 2.12a1 1 0 0 1 0-1.41l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a3 3 0 0 0 0 4.24l2.83 2.83a3 3 0 0 0 4.24 0l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a1 1 0 0 1-1.41 0l-2.83-2.83zm9.9-9.9a3 3 0 0 0-4.24 0l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a1 1 0 0 1 1.41 0l2.83 2.83a1 1 0 0 1 0 1.41l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a3 3 0 0 0 0-4.24l-2.83-2.83z"
    },
    {
      id: "custom2",
      label: "Custom 2",
      showKey: "showCustom2",
      handleKey: "custom2Handle",
      urlKey: "custom2Url",
      iconUrlKey: "custom2IconUrl",
      labelKey: "custom2Label",
      fa: "fa-solid fa-link",
      color: "#ffb36b",
      svg: "M10.59 13.41a1 1 0 0 1 0-1.41l4.24-4.24a1 1 0 1 1 1.41 1.41l-4.24 4.24a1 1 0 0 1-1.41 0zm-2.12 2.12a1 1 0 0 1 0-1.41l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a3 3 0 0 0 0 4.24l2.83 2.83a3 3 0 0 0 4.24 0l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a1 1 0 0 1-1.41 0l-2.83-2.83zm9.9-9.9a3 3 0 0 0-4.24 0l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a1 1 0 0 1 1.41 0l2.83 2.83a1 1 0 0 1 0 1.41l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a3 3 0 0 0 0-4.24l-2.83-2.83z"
    }
  ];

export const SOCIALS_VARIANTS = ["row", "pills", "cards", "stack", "neon", "minimal"];
  let socialsFaLoaded = false;

export function ensureSocialsFontAwesome() {
    if (socialsFaLoaded) return;
    if (document.getElementById("ccs-socials-fa")) {
      socialsFaLoaded = true;
      return;
    }
    const link = document.createElement("link");
    link.id = "ccs-socials-fa";
    link.rel = "stylesheet";
    link.href = "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.7.2/css/all.min.css";
    document.head.appendChild(link);
    socialsFaLoaded = true;
  }

export function socialsVariant(item) {
    const raw = String(prop(item, "variant", "row") || "row").toLowerCase();
    return SOCIALS_VARIANTS.includes(raw) ? raw : "row";
  }

export function socialsIconLibrary(item) {
    const raw = String(prop(item, "iconLibrary", "svg") || "svg").toLowerCase();
    return raw === "fontawesome" ? "fontawesome" : "svg";
  }

export function resolveSocialsEntries(item) {
    const platformId = String(prop(item, "platform", "") || "").trim().toLowerCase();
    if (platformId) {
      const platform = SOCIALS_PLATFORMS.find((p) => p.id === platformId) || SOCIALS_PLATFORMS[0];
      if (!platform) return [];
      const handle = String(
        prop(item, "handle", prop(item, platform.handleKey, "")) || ""
      ).trim();
      const urlRaw = String(
        prop(item, "url", prop(item, platform.urlKey, "")) || ""
      ).trim();
      const customIconUrl = String(
        prop(item, "iconUrl", prop(item, platform.iconUrlKey, "")) || ""
      ).trim();
      const labelDefault = platform.labelKey
        ? String(prop(item, platform.labelKey, platform.label) || platform.label)
        : platform.label;
      const label = String(prop(item, "label", labelDefault) || labelDefault).trim() || platform.label;
      let href = urlRaw;
      if (!href && handle && typeof platform.urlFromHandle === "function") {
        href = platform.urlFromHandle(handle);
      }
      return [{
        id: platform.id,
        label,
        handle,
        href,
        customIconUrl,
        fa: platform.fa,
        color: platform.color,
        svg: platform.svg
      }];
    }

    // Legacy: multi-platform props (showTwitch, …) for older layouts
    const entries = [];
    for (const platform of SOCIALS_PLATFORMS) {
      const enabledDefault = platform.id.indexOf("custom") === 0 ||
        platform.id === "kick" ||
        platform.id === "bluesky"
        ? false
        : true;
      if (prop(item, platform.showKey, enabledDefault) === false) continue;
      const handle = String(prop(item, platform.handleKey, "") || "").trim();
      const urlRaw = String(prop(item, platform.urlKey, "") || "").trim();
      const customIconUrl = String(prop(item, platform.iconUrlKey, "") || "").trim();
      const label = platform.labelKey
        ? String(prop(item, platform.labelKey, platform.label) || platform.label).trim() || platform.label
        : platform.label;
      let href = urlRaw;
      if (!href && handle && typeof platform.urlFromHandle === "function") {
        href = platform.urlFromHandle(handle);
      }
      entries.push({
        id: platform.id,
        label,
        handle,
        href,
        customIconUrl,
        fa: platform.fa,
        color: platform.color,
        svg: platform.svg
      });
    }
    return entries;
  }

export function renderSocialsIcon(entry, library) {
    if (entry.customIconUrl) {
      return `<img class="ccs-socials-icon ccs-socials-icon-img" src="${escapeHtml(entry.customIconUrl)}" alt="" />`;
    }
    if (library === "fontawesome" && entry.fa) {
      return `<i class="ccs-socials-icon ccs-socials-icon-fa ${escapeHtml(entry.fa)}" aria-hidden="true"></i>`;
    }
    return (
      `<svg class="ccs-socials-icon ccs-socials-icon-svg" viewBox="0 0 24 24" aria-hidden="true">` +
      `<path fill="currentColor" d="${entry.svg || ""}"></path></svg>`
    );
  }

export function createSocialsEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-socials";
    el.innerHTML = `<div class="ccs-socials-list"></div>`;
    if (typeof ResizeObserver !== "undefined") {
      el._ro = new ResizeObserver(() => fitSocials(el));
      el._ro.observe(el);
    }
    updateSocials(el, item);
    return el;
  }

export function applySocialsVariant(el, item) {
    const variant = socialsVariant(item);
    SOCIALS_VARIANTS.forEach((name) => {
      el.classList.remove("ccs-socials-v-" + name);
    });
    el.classList.add("ccs-socials-v-" + variant);
    el.dataset.variant = variant;
    const colorMode = String(prop(item, "colorMode", "brand") || "brand").toLowerCase();
    el.classList.toggle("ccs-socials-color-brand", colorMode !== "mono");
    el.classList.toggle("ccs-socials-color-mono", colorMode === "mono");
    el.classList.toggle("hide-labels", prop(item, "showLabels", true) === false);
    el.classList.toggle("hide-handles", prop(item, "showHandles", true) === false);
  }

export function fitSocials(el) {
    if (!el) return;
    const w = Math.max(1, el.clientWidth || el.offsetWidth || 720);
    const h = Math.max(1, el.clientHeight || el.offsetHeight || 96);
    const scale = Math.max(0.5, Math.min(1.4, Math.min(w / 720, h / 96)));
    el.style.setProperty("--ccs-socials-scale", String(scale));
  }

export function updateSocials(el, item) {
    if (!el) return;
    const library = socialsIconLibrary(item);
    if (library === "fontawesome") {
      ensureSocialsFontAwesome();
    }
    applySocialsVariant(el, item);
    const iconSize = Number(prop(item, "iconSize", 36)) || 36;
    const gap = Number(prop(item, "gap", 18)) || 18;
    const iconColor = String(prop(item, "iconColor", "#ffffff") || "#ffffff");
    el.style.setProperty("--ccs-socials-icon-size", iconSize + "px");
    el.style.setProperty("--ccs-socials-gap", gap + "px");
    el.style.setProperty("--ccs-socials-icon-color", iconColor);

    const list = el.querySelector(".ccs-socials-list");
    if (!list) return;
    const entries = resolveSocialsEntries(item);
    list.innerHTML = entries.map((entry) => {
      const handleText = entry.handle
        ? (entry.handle.indexOf("@") === 0 || entry.id === "discord" ? entry.handle : "@" + entry.handle)
        : "";
      const style = entry.color ? `--ccs-socials-brand:${entry.color}` : "";
      const hrefAttr = entry.href ? ` data-href="${escapeHtml(entry.href)}"` : "";
      return (
        `<div class="ccs-socials-item" data-id="${escapeHtml(entry.id)}" style="${style}"${hrefAttr}>` +
        `<div class="ccs-socials-glyph">${renderSocialsIcon(entry, library)}</div>` +
        `<div class="ccs-socials-meta">` +
        `<div class="ccs-socials-label">${escapeHtml(entry.label)}</div>` +
        `<div class="ccs-socials-handle">${escapeHtml(handleText)}</div>` +
        `</div></div>`
      );
    }).join("");
    fitSocials(el);
    requestAnimationFrame(() => fitSocials(el));
  }

export function createTextEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-text";
    el.innerHTML = `<div class="ccs-text-content"></div>`;
    updateText(el, item);
    return el;
  }

export function updateText(el, item) {
    const content = el.querySelector(".ccs-text-content");
    if (!content) return;
    const text = String(prop(item, "content", "Text") ?? "");
    content.textContent = text;
    const align = String(prop(item, "align", "center") || "center");
    const vertical = String(prop(item, "verticalAlign", "middle") || "middle");
    el.style.setProperty("--ccs-text-size", (Number(prop(item, "fontSizePx", 48)) || 48) + "px");
    el.style.setProperty("--ccs-text-family", String(prop(item, "fontFamily", "Segoe UI, system-ui, sans-serif")));
    el.style.setProperty("--ccs-text-color", String(prop(item, "color", "#ffffff")));
    el.style.setProperty("--ccs-text-weight", String(prop(item, "fontWeight", "700")));
    el.style.setProperty("--ccs-text-tracking", (Number(prop(item, "letterSpacingPx", 0)) || 0) + "px");
    el.style.setProperty("--ccs-text-leading", String(prop(item, "lineHeight", 1.15)));
    el.style.setProperty("--ccs-text-shadow", String(prop(item, "textShadow", "0 2px 12px rgba(0,0,0,.55)")));
    el.style.setProperty("--ccs-text-align", align);
    const justify =
      vertical === "top" ? "flex-start" :
      vertical === "bottom" ? "flex-end" : "center";
    el.style.setProperty("--ccs-text-justify", justify);
    content.style.textAlign = align;
  }

export function createImageEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-image";
    el.innerHTML =
      `<img class="ccs-image-media" alt="" draggable="false"/>` +
      `<div class="ccs-image-placeholder">Bild-URL setzen</div>`;
    updateImage(el, item);
    return el;
  }

export function updateImage(el, item) {
    const img = el.querySelector(".ccs-image-media");
    const placeholder = el.querySelector(".ccs-image-placeholder");
    if (!img) return;
    const src = String(prop(item, "src", "") || "").trim();
    const fit = String(prop(item, "fit", "contain") || "contain");
    const opacity = Math.max(0, Math.min(1, Number(prop(item, "opacity", 1))));
    const radius = Math.max(0, Number(prop(item, "borderRadiusPx", 0)) || 0);
    const position = String(prop(item, "objectPosition", "center") || "center");
    el.style.setProperty("--ccs-image-radius", radius + "px");
    el.style.setProperty("--ccs-image-opacity", String(Number.isFinite(opacity) ? opacity : 1));
    img.style.objectFit = ["contain", "cover", "fill", "none", "scale-down"].includes(fit) ? fit : "contain";
    img.style.objectPosition = position;
    if (src) {
      if (img.getAttribute("src") !== src) {
        img.setAttribute("src", src);
      }
      el.classList.add("has-src");
      if (placeholder) placeholder.hidden = true;
    } else {
      img.removeAttribute("src");
      el.classList.remove("has-src");
      if (placeholder) placeholder.hidden = false;
    }
  }

export const COUNTDOWN_VARIANTS = ["classic", "neon", "minimal", "bold"];

export function countdownVariant(item) {
    const raw = String(prop(item, "variant", "classic") || "classic").toLowerCase();
    return COUNTDOWN_VARIANTS.includes(raw) ? raw : "classic";
  }

export function formatCountdownSeconds(totalSeconds, format) {
    const s = Math.max(0, Math.floor(Number(totalSeconds) || 0));
    const fmt = String(format || "mm:ss").toLowerCase();
    if (fmt === "ss") {
      return String(s);
    }
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    if (fmt === "hh:mm:ss" || h > 0) {
      return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
    }
    return `${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
  }

export function resolveCountdownRemaining(data) {
    const countdown = (data && data.countdown) || {};
    if (countdown.endsAt) {
      const ends = Date.parse(countdown.endsAt);
      if (!Number.isNaN(ends)) {
        return Math.max(0, Math.ceil((ends - Date.now()) / 1000));
      }
    }
    return Math.max(0, Math.floor(Number(countdown.remainingSeconds) || 0));
  }

export function createCountdownEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-countdown";
    el.innerHTML =
      `<div class="ccs-countdown-label"></div>` +
      `<div class="ccs-countdown-value">00:00</div>`;
    applyCountdownAppearance(el, item);
    if (typeof ResizeObserver !== "undefined") {
      el._ro = new ResizeObserver(() => fitCountdown(el));
      el._ro.observe(el);
    }
    requestAnimationFrame(() => fitCountdown(el));
    return el;
  }

export function applyCountdownAppearance(el, item) {
    const variant = countdownVariant(item);
    COUNTDOWN_VARIANTS.forEach((name) => {
      el.classList.remove("ccs-countdown-v-" + name);
    });
    el.classList.add("ccs-countdown-v-" + variant);
    el.dataset.variant = variant;
    el.classList.toggle("hide-label", prop(item, "showLabel", true) === false);
    const align = String(prop(item, "align", "center") || "center");
    el.style.setProperty("--ccs-countdown-align", align);
    el.style.setProperty("--ccs-countdown-color", String(prop(item, "color", "#ffffff")));
    el.style.setProperty("--ccs-countdown-size", (Number(prop(item, "fontSizePx", 72)) || 72) + "px");
  }

export function fitCountdown(el) {
    if (!el) return;
    const w = Math.max(1, el.clientWidth || el.offsetWidth || 520);
    const h = Math.max(1, el.clientHeight || el.offsetHeight || 160);
    const scale = Math.max(0.4, Math.min(1.4, Math.min(w / 520, h / 160)));
    el.style.setProperty("--ccs-countdown-scale", String(scale));
  }

export function updateCountdown(el, item, data) {
    applyCountdownAppearance(el, item);
    fitCountdown(el);
    paintCountdown(el, item, data);
  }

export function paintCountdown(el, item, data) {
    const countdown = (data && data.countdown) || {};
    const running = countdown.isRunning === true;
    const remaining = resolveCountdownRemaining(data);
    const idle = !running && remaining <= 0;
    el.classList.toggle("is-running", running);
    el.classList.toggle("is-idle", idle);
    el.classList.toggle("is-hidden", idle && prop(item, "hideWhenIdle", false) === true);
    const labelEl = el.querySelector(".ccs-countdown-label");
    const valueEl = el.querySelector(".ccs-countdown-value");
    if (labelEl) {
      labelEl.textContent = String(countdown.label || "Countdown");
    }
    if (valueEl) {
      valueEl.textContent = formatCountdownSeconds(remaining, prop(item, "format", "mm:ss"));
    }
  }

export function enqueueAlert(el, item, payload) {
    const max = Number(prop(item, "maxQueue", 5)) || 5;
    el._queue.push(payload);
    while (el._queue.length > max) {
      el._queue.shift();
    }
    pumpAlert(el, item);
  }

export function pumpAlert(el, item) {
    if (el._showing || !el._queue.length) {
      return;
    }
    el._showing = true;
    const next = el._queue.shift();
    const card = el.querySelector(".ccs-alert-card");
    card.querySelector(".ccs-alert-type").textContent = (next.alertType || "ALERT").toUpperCase();
    card.querySelector(".ccs-alert-user").textContent = next.user || "";
    card.querySelector(".ccs-alert-summary").textContent = next.summary || "";
    card.classList.add("show");
    const duration = Number(prop(item, "durationMs", 5000)) || 5000;
    clearTimeout(el._hideTimer);
    el._hideTimer = setTimeout(() => {
      card.classList.remove("show");
      setTimeout(() => {
        el._showing = false;
        pumpAlert(el, item);
      }, 360);
    }, duration);
  }

export function createSpotifyEl() {
    const el = document.createElement("div");
    el.className = "ccs-spotify ccs-music";
    el.innerHTML =
      `<div class="ccs-spotify-content">` +
      `<div class="ccs-spotify-cover"></div>` +
      `<div class="ccs-spotify-info">` +
      `<div class="ccs-spotify-topline">` +
      `<div class="ccs-spotify-heading">MUSIC Â· NOW PLAYING</div>` +
      `<div class="ccs-spotify-status">SPIELT</div>` +
      `</div>` +
      `<div class="ccs-spotify-title">-</div>` +
      `<div class="ccs-spotify-artist">-</div>` +
      `<div class="ccs-spotify-album"></div>` +
      `<div class="ccs-spotify-progress-row">` +
      `<div class="ccs-spotify-progress-track"><div class="ccs-spotify-progress"></div></div>` +
      `<div class="ccs-spotify-time">00:00 / 00:00</div>` +
      `</div></div></div>`;
    el._progressBase = 0;
    el._progressAt = Date.now();
    el._duration = 0;
    el._playing = false;
    return el;
  }

export function resolveMusicState(data) {
    const music = (data && data.music) || {};
    const spotify = (data && data.spotify) || {};
    // music hat Vorrang, wenn vorhanden; sonst spotify (DenverJohn-KompatibilitÃ¤t)
    const hasMusic = music && (music.title || music.artist || music.connected === true || music.provider);
    return hasMusic ? music : spotify;
  }

export function providerHeading(music) {
    const name = (music.providerDisplayName || "").trim();
    if (name) {
      return name.toUpperCase() + " Â· NOW PLAYING";
    }
    const id = (music.provider || "").toLowerCase();
    if (id === "ytmusic") {
      return "YOUTUBE MUSIC Â· NOW PLAYING";
    }
    if (id === "spotify") {
      return "SPOTIFY Â· NOW PLAYING";
    }
    return "MUSIC Â· NOW PLAYING";
  }

export function updateSpotify(el, item, data) {
    const music = resolveMusicState(data);
    const showTitle = prop(item, "showTitle", music.showTitle !== false);
    const showArtist = prop(item, "showArtist", music.showArtist !== false);
    const showCover = prop(item, "showAlbumCover", music.showAlbumCover !== false);
    const showProgress = prop(item, "showProgress", music.showProgress !== false);
    const hideWhenPaused = prop(item, "hideWhenPaused", music.hideWhenPaused === true);

    const hasSong = Boolean(music.title || music.artist);
    const connected = music.connected === true;
    const showOverlay = music.showInOverlay !== false;
    let show = showOverlay && connected && hasSong;
    if (show && hideWhenPaused && !music.isPlaying) {
      show = false;
    }

    el.classList.toggle("visible", show);
    el.classList.toggle("paused", !music.isPlaying);

    const content = el.querySelector(".ccs-spotify-content");
    content.classList.toggle("no-cover", !showCover);
    const cover = el.querySelector(".ccs-spotify-cover");
    cover.style.display = showCover ? "" : "none";
    const coverUrl = music.cover || music.coverUrl || "";
    cover.style.backgroundImage = coverUrl ? `url("${coverUrl}")` : "none";

    el.querySelector(".ccs-spotify-heading").textContent = providerHeading(music);
    el.querySelector(".ccs-spotify-title").style.display = showTitle ? "" : "none";
    el.querySelector(".ccs-spotify-artist").style.display = showArtist ? "" : "none";
    el.querySelector(".ccs-spotify-album").style.display = showArtist ? "" : "none";
    el.querySelector(".ccs-spotify-progress-row").style.display = showProgress ? "" : "none";

    el.querySelector(".ccs-spotify-title").textContent = music.title || "Unbekannter Titel";
    el.querySelector(".ccs-spotify-artist").textContent = music.artist || "Unbekannter KÃ¼nstler";
    el.querySelector(".ccs-spotify-album").textContent = music.album || "";
    el.querySelector(".ccs-spotify-status").textContent = music.isPlaying ? "SPIELT" : "PAUSIERT";

    el._progressBase = Math.max(0, Number(music.progressMs) || 0);
    el._progressAt = Date.now();
    el._duration = Math.max(0, Number(music.durationMs) || 0);
    el._playing = music.isPlaying === true;
    paintSpotifyProgress(el);
  }

export function paintSpotifyProgress(el) {
    let current = el._progressBase || 0;
    if (el._playing) {
      current += Date.now() - (el._progressAt || Date.now());
    }
    const duration = el._duration || 0;
    const percent = duration > 0 ? Math.min(100, (current / duration) * 100) : 0;
    el.querySelector(".ccs-spotify-progress").style.width = `${percent}%`;
    el.querySelector(".ccs-spotify-time").textContent =
      `${formatMs(current)} / ${formatMs(duration)}`;
  }

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

export function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

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

export function isShapeItem(item) {
    const type = (item && item.type) || "";
    return item.kind === "shape" || type.startsWith("frame.") || type.startsWith("shape.") || !!SHAPE_DEFAULTS[type];
  }

export function shapeClass(type, item) {
    switch (type) {
      case "frame.rect": return "ccs-shape ccs-frame-rect";
      case "frame.circle": return "ccs-shape ccs-frame-circle";
      case "frame.corners": return "ccs-shape ccs-frame-corners";
      case "frame.bevel": return "ccs-shape ccs-frame-bevel";
      case "frame.neon": return "ccs-shape ccs-frame-neon";
      case "frame.dashed": return "ccs-shape ccs-frame-dashed";
      case "frame.card": {
        let variant = String(prop(item, "variant", "classic") || "classic").toLowerCase();
        if (CARD_FRAME_VARIANTS.indexOf(variant) < 0) variant = "classic";
        return "ccs-shape ccs-frame-card ccs-frame-card-v-" + variant;
      }
      case "shape.vignette": return "ccs-shape ccs-shape-vignette";
      case "shape.scene-bg": return "ccs-shape ccs-shape-scene-bg";
      default: return "ccs-shape ccs-frame-rect";
    }
  }

export function applyCardFrame(el, item) {
    const color = prop(item, "color", "#ff7a00");
    const color2 = prop(item, "color2", "#ffb36b");
    let fillOpacity = Number(prop(item, "fillOpacity", 0.18));
    if (Number.isNaN(fillOpacity)) fillOpacity = 0.18;
    fillOpacity = Math.max(0, Math.min(1, fillOpacity));
    const showSweep = prop(item, "showSweep", true) !== false;
    const showLines = prop(item, "showLines", true) !== false;

    el.style.setProperty("--frame-color", color);
    el.style.setProperty("--frame-color2", color2);
    el.style.setProperty("--frame-fill-opacity", String(fillOpacity));
    el.dataset.sweep = showSweep ? "1" : "0";
    el.dataset.lines = showLines ? "1" : "0";

    const sweep = el.querySelector(".ccs-frame-card-sweep");
    const topline = el.querySelector(".ccs-frame-card-topline");
    const bottomline = el.querySelector(".ccs-frame-card-bottomline");
    if (sweep) sweep.style.display = showSweep ? "" : "none";
    if (topline) topline.style.display = showLines ? "" : "none";
    if (bottomline) bottomline.style.display = showLines ? "" : "none";
  }

export function parseHexColor(color) {
    if (!color) return null;
    let c = String(color).trim();
    if (c[0] === "#") c = c.slice(1);
    if (c.length === 3) c = c[0] + c[0] + c[1] + c[1] + c[2] + c[2];
    if (!/^[0-9a-fA-F]{6}$/.test(c)) return null;
    return {
      r: parseInt(c.slice(0, 2), 16),
      g: parseInt(c.slice(2, 4), 16),
      b: parseInt(c.slice(4, 6), 16)
    };
  }

export function rgbaFrom(color, alpha) {
    const rgb = parseHexColor(color);
    if (!rgb) return `rgba(255,122,0,${alpha})`;
    return `rgba(${rgb.r},${rgb.g},${rgb.b},${alpha})`;
  }

export function resolveSceneBgConfig(item) {
    const props = (item && item.props) || {};
    const name = String(props.preset || "ember").toLowerCase();
    const preset = SCENE_BG_PRESETS[name] || SCENE_BG_PRESETS.ember;
    const merged = Object.assign({}, preset, props);
    const speed = Number(merged.speed);
    if (!Number.isNaN(speed) && speed > 0) {
      const baseDrift = props.driftDuration != null
        ? Number(props.driftDuration)
        : (preset.driftDuration || 18);
      const baseParticle = props.particleDuration != null
        ? Number(props.particleDuration)
        : (preset.particleDuration || 22);
      // Explicit durations are absolute; otherwise speed scales the preset.
      if (props.driftDuration == null) {
        merged.driftDuration = Math.round(baseDrift / speed * 10) / 10;
      }
      if (props.particleDuration == null) {
        merged.particleDuration = Math.round(baseParticle / speed * 10) / 10;
      }
    }
    return merged;
  }

export function applySceneBg(el, item) {
    const cfg = resolveSceneBgConfig(item);
    const glow1 = cfg.glow1 || "#ff7a00";
    const glow2 = cfg.glow2 || "#ffb36b";
    const stripeColor = cfg.stripeColor || glow1;
    const particleColor = cfg.particleColor || glow1;
    const glow1Opacity = cfg.glow1Opacity != null ? Number(cfg.glow1Opacity) : 0.18;
    const glow2Opacity = cfg.glow2Opacity != null ? Number(cfg.glow2Opacity) : 0.1;
    const stripeOpacity = cfg.stripeOpacity != null ? Number(cfg.stripeOpacity) : 0.065;
    const scanOpacity = cfg.scanOpacity != null ? Number(cfg.scanOpacity) : 0;
    const particleOpacity = cfg.particleOpacity != null ? Number(cfg.particleOpacity) : 0.34;
    const vignetteOpacity = cfg.vignetteOpacity != null ? Number(cfg.vignetteOpacity) : 0;
    const driftDuration = cfg.driftDuration != null ? Number(cfg.driftDuration) : 18;
    const particleDuration = cfg.particleDuration != null ? Number(cfg.particleDuration) : 22;
    const scanDuration = cfg.scanDuration != null ? Number(cfg.scanDuration) : 7;
    const sat = cfg.sat != null ? Number(cfg.sat) : 1;
    const brightness = cfg.brightness != null ? Number(cfg.brightness) : 1;

    el.style.setProperty("--ccs-bg-base", cfg.bgBase || "#030303");
    el.style.setProperty("--ccs-bg-mid", cfg.bgMid || "#101010");
    el.style.setProperty("--ccs-bg-deep", cfg.bgDeep || "#1a0d03");
    el.style.setProperty("--ccs-bg-glow1", rgbaFrom(glow1, glow1Opacity));
    el.style.setProperty("--ccs-bg-glow2", rgbaFrom(glow2, glow2Opacity));
    el.style.setProperty("--ccs-bg-glow1-x", cfg.glow1X || "18%");
    el.style.setProperty("--ccs-bg-glow1-y", cfg.glow1Y || "22%");
    el.style.setProperty("--ccs-bg-glow1-size", cfg.glow1Size || "30%");
    el.style.setProperty("--ccs-bg-glow2-x", cfg.glow2X || "76%");
    el.style.setProperty("--ccs-bg-glow2-y", cfg.glow2Y || "68%");
    el.style.setProperty("--ccs-bg-glow2-size", cfg.glow2Size || "34%");
    el.style.setProperty("--ccs-bg-stripe", rgbaFrom(stripeColor, stripeOpacity));
    el.style.setProperty("--ccs-bg-stripe-angle", cfg.stripeAngle || "115deg");
    el.style.setProperty("--ccs-bg-stripe-gap", (cfg.stripeGap != null ? cfg.stripeGap : 92) + (typeof cfg.stripeGap === "string" ? "" : "px"));
    el.style.setProperty("--ccs-bg-stripe-width", (cfg.stripeWidth != null ? cfg.stripeWidth : 3) + (typeof cfg.stripeWidth === "string" ? "" : "px"));
    el.style.setProperty("--ccs-bg-stripe-period", (cfg.stripePeriod != null ? cfg.stripePeriod : 180) + (typeof cfg.stripePeriod === "string" ? "" : "px"));
    el.style.setProperty("--ccs-bg-particle", rgbaFrom(particleColor, 0.7));
    el.style.setProperty("--ccs-bg-particle-opacity", String(particleOpacity));
    el.style.setProperty("--ccs-bg-particle-size", cfg.particleSize || "72px");
    el.style.setProperty("--ccs-bg-particle-dot", cfg.particleDot || "1px");
    el.style.setProperty("--ccs-bg-drift-duration", driftDuration + "s");
    el.style.setProperty("--ccs-bg-particle-duration", particleDuration + "s");
    el.style.setProperty("--ccs-bg-drift-from", cfg.driftFrom || "-4%");
    el.style.setProperty("--ccs-bg-drift-to", cfg.driftTo || "8%");
    el.style.setProperty("--ccs-bg-particle-x", (cfg.particleX != null ? cfg.particleX : 140) + (typeof cfg.particleX === "string" ? "" : "px"));
    el.style.setProperty("--ccs-bg-particle-y", (cfg.particleY != null ? cfg.particleY : 90) + (typeof cfg.particleY === "string" ? "" : "px"));
    el.style.setProperty("--ccs-bg-vignette-opacity", String(vignetteOpacity));
    el.style.setProperty("--ccs-bg-scan", rgbaFrom(glow1, scanOpacity));
    el.style.setProperty("--ccs-bg-scan-duration", scanDuration + "s");
    el.style.setProperty("--ccs-bg-hue", cfg.hueShift || "0deg");
    el.style.setProperty("--ccs-bg-sat", String(sat));
    el.style.setProperty("--ccs-bg-brightness", String(brightness));

    const stripesOff = cfg.stripes === false || cfg.stripes === "0" || cfg.stripes === 0;
    const particlesOff = cfg.particles === false || cfg.particles === "0" || cfg.particles === 0;
    const paused = cfg.paused === true || cfg.paused === "1" || cfg.paused === 1;
    el.dataset.stripes = stripesOff ? "0" : "1";
    el.dataset.particles = particlesOff ? "0" : "1";
    el.dataset.paused = paused ? "1" : "0";
  }

export function createShapeEl(item) {
    const el = document.createElement("div");
    el.className = shapeClass(item.type, item);
    if (item.type === "shape.scene-bg") {
      applySceneBg(el, item);
      return el;
    }
    if (item.type === "frame.card") {
      const sweep = document.createElement("div");
      sweep.className = "ccs-frame-card-sweep";
      const topline = document.createElement("div");
      topline.className = "ccs-frame-card-topline";
      const bottomline = document.createElement("div");
      bottomline.className = "ccs-frame-card-bottomline";
      el.appendChild(sweep);
      el.appendChild(topline);
      el.appendChild(bottomline);
      applyCardFrame(el, item);
      return el;
    }
    const color = prop(item, "color", "#ff7a00");
    el.style.setProperty("--frame-color", color);
    if (item.type === "frame.rect") {
      el.style.setProperty("--frame-radius", (prop(item, "radius", 16) || 16) + "px");
    }
    return el;
  }

export function createItemContent(item) {
    if (isShapeItem(item)) {
      return createShapeEl(item);
    }
    if (item.type === "online") return createOnlineEl(item);
    if (item.type === "alert") return createAlertEl(item);
    if (item.type === "music" || item.type === "spotify") return createSpotifyEl(item);
    if (item.type === "chat") return createChatEl(item);
    if (item.type === "ending-stats") return createEndingStatsEl(item);
    if (item.type === "text") return createTextEl(item);
    if (item.type === "image") return createImageEl(item);
    if (item.type === "countdown") return createCountdownEl(item);
    if (item.type === "socials") return createSocialsEl(item);
    const unknown = document.createElement("div");
    unknown.textContent = item.type || "unknown";
    unknown.style.padding = "12px";
    unknown.style.background = "rgba(0,0,0,.5)";
    return unknown;
  }
