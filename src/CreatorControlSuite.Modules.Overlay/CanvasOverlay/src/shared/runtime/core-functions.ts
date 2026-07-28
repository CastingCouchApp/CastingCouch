import type { LayoutItem } from '../types';
import { prop } from '../utils/prop';
import { formatClock, formatUptime } from '../utils/format';
import { rgbaFrom } from '../utils/color';
import { SCENE_BG_PRESETS } from '../defaults/scene-bg';
import { CARD_FRAME_VARIANTS, SHAPE_DEFAULTS } from '../defaults/shapes';
import { createCutoutEl } from '../shapes/cutout';
import { createFrameEl, isUnifiedFrameType, resolveFrameMode } from '../shapes/frame';
import {
  createSpotifyEl,
  updateSpotify,
  paintSpotifyProgress,
  applyMusicVariant,
  applyMusicSize,
  fitMusic,
  syncMusicMarquee,
  resolveMusicState,
  providerHeading,
  MUSIC_VARIANTS,
  MUSIC_SIZE_PRESETS
} from '../widgets/music';
import {
  createPartnerRouletteEl,
  updatePartnerRoulette,
  resolvePartnerRouletteImages,
  PARTNER_ROULETTE_TRANSITIONS
} from '../widgets/partner-roulette';
import {
  createGoalBarEl,
  updateGoalBar,
  GOAL_BAR_VARIANTS,
  GOAL_BAR_SIZE_PRESETS
} from '../widgets/goal-bar';
import {
  createEventTickerEl,
  updateEventTicker,
  pushEventTickerItem,
  EVENT_TICKER_VARIANTS,
  EVENT_TICKER_SIZE_PRESETS,
  EVENT_TICKER_SOURCES
} from '../widgets/event-ticker';
import {
  createViewerCountEl,
  updateViewerCount,
  VIEWER_COUNT_VARIANTS,
  VIEWER_COUNT_SIZE_PRESETS
} from '../widgets/viewer-count';
import {
  createLowerThirdEl,
  updateLowerThird,
  LOWER_THIRD_VARIANTS,
  LOWER_THIRD_SIZE_PRESETS
} from '../widgets/lower-third';
import {
  createQrCodeEl,
  updateQrCode,
  QR_CODE_VARIANTS,
  QR_CODE_SIZE_PRESETS
} from '../widgets/qr-code';
import {
  createBrbPanelEl,
  updateBrbPanel,
  paintBrbPanel,
  BRB_PANEL_VARIANTS,
  BRB_PANEL_SIZE_PRESETS,
  BRB_PANEL_MODES
} from '../widgets/brb-panel';
import {
  createAnnouncementBarEl,
  updateAnnouncementBar,
  ANNOUNCEMENT_BAR_VARIANTS,
  ANNOUNCEMENT_BAR_SIZE_PRESETS
} from '../widgets/announcement-bar';
import {
  createBubatzCantinaEl,
  updateBubatzCantina,
  BUBATZ_CANTINA_VARIANTS,
  BUBATZ_CANTINA_SIZE_PRESETS,
  BUBATZ_CANTINA_MODES
} from '../widgets/bubatz-cantina';
import {
  createFruppisLandadelEl,
  updateFruppisLandadel,
  FRUPPIS_LANDADEL_VARIANTS,
  FRUPPIS_LANDADEL_SIZE_PRESETS
} from '../widgets/fruppis-landadel';
import {
  createAnimatedBackgroundEl,
  updateAnimatedBackground,
  ANIMATED_BACKGROUND_VARIANTS,
  ANIMATED_BACKGROUND_SIZE_PRESETS,
  ANIMATED_BACKGROUND_VARIANT_LABELS
} from '../widgets/animated-background';
import {
  createDividerEl,
  updateDivider,
  DIVIDER_VARIANTS,
  DIVIDER_STYLES,
  DIVIDER_SIZE_PRESETS
} from '../shapes/divider';
import {
  createCamRingEl,
  updateCamRing,
  CAM_RING_VARIANTS,
  CAM_RING_SIZE_PRESETS
} from '../shapes/cam-ring';
import {
  createStickerEl,
  updateSticker,
  STICKER_PRESETS,
  STICKER_VARIANTS
} from '../shapes/sticker';
import { createSocialsEl } from '../widgets/socials';
import { createChatEl } from '../widgets/chat';

export {
  createSpotifyEl,
  updateSpotify,
  paintSpotifyProgress,
  applyMusicVariant,
  applyMusicSize,
  fitMusic,
  syncMusicMarquee,
  resolveMusicState,
  providerHeading,
  MUSIC_VARIANTS,
  MUSIC_SIZE_PRESETS
};

export {
  createPartnerRouletteEl,
  updatePartnerRoulette,
  resolvePartnerRouletteImages,
  PARTNER_ROULETTE_TRANSITIONS
};

export {
  createGoalBarEl,
  updateGoalBar,
  GOAL_BAR_VARIANTS,
  GOAL_BAR_SIZE_PRESETS,
  createEventTickerEl,
  updateEventTicker,
  pushEventTickerItem,
  EVENT_TICKER_VARIANTS,
  EVENT_TICKER_SIZE_PRESETS,
  EVENT_TICKER_SOURCES,
  createViewerCountEl,
  updateViewerCount,
  VIEWER_COUNT_VARIANTS,
  VIEWER_COUNT_SIZE_PRESETS,
  createLowerThirdEl,
  updateLowerThird,
  LOWER_THIRD_VARIANTS,
  LOWER_THIRD_SIZE_PRESETS,
  createQrCodeEl,
  updateQrCode,
  QR_CODE_VARIANTS,
  QR_CODE_SIZE_PRESETS,
  createBrbPanelEl,
  updateBrbPanel,
  paintBrbPanel,
  BRB_PANEL_VARIANTS,
  BRB_PANEL_SIZE_PRESETS,
  BRB_PANEL_MODES,
  createAnnouncementBarEl,
  updateAnnouncementBar,
  ANNOUNCEMENT_BAR_VARIANTS,
  ANNOUNCEMENT_BAR_SIZE_PRESETS,
  createBubatzCantinaEl,
  updateBubatzCantina,
  BUBATZ_CANTINA_VARIANTS,
  BUBATZ_CANTINA_SIZE_PRESETS,
  BUBATZ_CANTINA_MODES,
  createFruppisLandadelEl,
  updateFruppisLandadel,
  FRUPPIS_LANDADEL_VARIANTS,
  FRUPPIS_LANDADEL_SIZE_PRESETS,
  createAnimatedBackgroundEl,
  updateAnimatedBackground,
  ANIMATED_BACKGROUND_VARIANTS,
  ANIMATED_BACKGROUND_SIZE_PRESETS,
  ANIMATED_BACKGROUND_VARIANT_LABELS,
  createDividerEl,
  updateDivider,
  DIVIDER_VARIANTS,
  DIVIDER_STYLES,
  DIVIDER_SIZE_PRESETS,
  createCamRingEl,
  updateCamRing,
  CAM_RING_VARIANTS,
  CAM_RING_SIZE_PRESETS,
  createStickerEl,
  updateSticker,
  STICKER_PRESETS,
  STICKER_VARIANTS
};

export * from '../widgets/socials';
export * from '../widgets/chat';

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

export function isShapeItem(item) {
    const type = (item && item.type) || "";
    return item.kind === "shape" || type === "frame" || type.startsWith("frame.") || type.startsWith("shape.") || !!SHAPE_DEFAULTS[type];
  }

export function shapeClass(type, item) {
    if (isUnifiedFrameType(type)) {
      const mode = resolveFrameMode(item);
      return "ccs-shape ccs-frame ccs-frame-m-" + mode;
    }
    switch (type) {
      case "frame.card": {
        let variant = String(prop(item, "variant", "classic") || "classic").toLowerCase();
        if (CARD_FRAME_VARIANTS.indexOf(variant) < 0) variant = "classic";
        return "ccs-shape ccs-frame-card ccs-frame-card-v-" + variant;
      }
      case "shape.vignette": return "ccs-shape ccs-shape-vignette";
      case "shape.scene-bg": return "ccs-shape ccs-shape-scene-bg";
      case "shape.cutout": return "ccs-shape ccs-shape-cutout";
      case "shape.divider": return "ccs-shape ccs-divider";
      case "shape.cam-ring": return "ccs-shape ccs-cam-ring";
      case "shape.sticker": return "ccs-shape ccs-sticker";
      default: return "ccs-shape ccs-frame ccs-frame-m-rect";
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
    if (item.type === "shape.cutout") {
      return createCutoutEl(item);
    }
    if (item.type === "shape.divider") {
      return createDividerEl(item);
    }
    if (item.type === "shape.cam-ring") {
      return createCamRingEl(item);
    }
    if (item.type === "shape.sticker") {
      return createStickerEl(item);
    }
    if (isUnifiedFrameType(item.type)) {
      return createFrameEl(item);
    }
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
    if (item.type === "partner-roulette") return createPartnerRouletteEl(item);
    if (item.type === "goal-bar") return createGoalBarEl(item);
    if (item.type === "event-ticker") return createEventTickerEl(item);
    if (item.type === "viewer-count") return createViewerCountEl(item);
    if (item.type === "lower-third") return createLowerThirdEl(item);
    if (item.type === "qr-code") return createQrCodeEl(item);
    if (item.type === "brb-panel") return createBrbPanelEl(item);
    if (item.type === "announcement-bar") return createAnnouncementBarEl(item);
    if (item.type === "bubatz-cantina") return createBubatzCantinaEl(item);
    if (item.type === "fruppis-landadel") return createFruppisLandadelEl(item);
    if (item.type === "animated-background") return createAnimatedBackgroundEl(item);
    const unknown = document.createElement("div");
    unknown.textContent = item.type || "unknown";
    unknown.style.padding = "12px";
    unknown.style.background = "rgba(0,0,0,.5)";
    return unknown;
  }
