import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./brb-panel.css";

export const BRB_PANEL_VARIANTS = [
  "classic",
  "neon",
  "glass",
  "cyber",
  "minimal",
  "bold",
  "soft",
  "outline",
  "broadcast",
  "poster",
  "card",
  "split-hero",
  "hud",
  "tape"
] as const;

export type BrbPanelVariant = (typeof BRB_PANEL_VARIANTS)[number];

export const BRB_PANEL_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  compact: { w: 720, h: 280, label: "Compact" },
  standard: { w: 900, h: 360, label: "Standard" },
  wide: { w: 1200, h: 380, label: "Wide" },
  poster: { w: 960, h: 540, label: "Poster" },
  brb: { w: 1060, h: 420, label: "BRB" }
};

export const BRB_PANEL_MODES = ["brb", "starting", "tech-pause", "custom"] as const;
export type BrbPanelMode = (typeof BRB_PANEL_MODES)[number];

const MODE_DEFAULT_TITLES: Record<BrbPanelMode, string> = {
  brb: "Be right back",
  starting: "Starting soon",
  "tech-pause": "Technical pause",
  custom: ""
};

type BrbPanelEl = HTMLElement & {
  _duration?: number;
  _endsAt?: string | null;
};

function panelVariant(item: LayoutItem | null | undefined): BrbPanelVariant {
  const raw = String(prop(item, "variant", "classic") || "classic").toLowerCase();
  return (BRB_PANEL_VARIANTS as readonly string[]).includes(raw) ? (raw as BrbPanelVariant) : "classic";
}

function panelMode(item: LayoutItem | null | undefined): BrbPanelMode {
  const raw = String(prop(item, "mode", "brb") || "brb").toLowerCase();
  return (BRB_PANEL_MODES as readonly string[]).includes(raw) ? (raw as BrbPanelMode) : "brb";
}

function formatCountdownSeconds(totalSeconds: number, format: string): string {
  const s = Math.max(0, Math.floor(Number(totalSeconds) || 0));
  const fmt = String(format || "mm:ss").toLowerCase();
  if (fmt === "ss") return String(s);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (fmt === "hh:mm:ss" || h > 0) {
    return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
  }
  return `${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
}

function resolveCountdownRemaining(data: Record<string, unknown> | null | undefined): number {
  const countdown = ((data && data.countdown) || {}) as Record<string, unknown>;
  if (countdown.endsAt) {
    const ends = Date.parse(String(countdown.endsAt));
    if (!Number.isNaN(ends)) {
      return Math.max(0, Math.ceil((ends - Date.now()) / 1000));
    }
  }
  return Math.max(0, Math.floor(Number(countdown.remainingSeconds) || 0));
}

function resolveTitle(item: LayoutItem): string {
  const explicit = String(prop(item, "title", "") || "").trim();
  if (explicit) return explicit;
  return MODE_DEFAULT_TITLES[panelMode(item)] || "";
}

function applyBrbPanelAppearance(el: HTMLElement, item: LayoutItem): void {
  const variant = panelVariant(item);
  BRB_PANEL_VARIANTS.forEach((name) => el.classList.remove("ccs-brb-panel-v-" + name));
  el.classList.add("ccs-brb-panel-v-" + variant);
  el.dataset.variant = variant;
  el.dataset.mode = panelMode(item);

  const sizeKey = String(prop(item, "sizePreset", "standard") || "standard").toLowerCase();
  Object.keys(BRB_PANEL_SIZE_PRESETS).forEach((name) => el.classList.remove("ccs-brb-panel-s-" + name));
  if (BRB_PANEL_SIZE_PRESETS[sizeKey]) {
    el.classList.add("ccs-brb-panel-s-" + sizeKey);
    el.dataset.sizePreset = sizeKey;
  }

  const align = String(prop(item, "align", "center") || "center");
  el.style.setProperty("--ccs-brb-align", align);
  el.style.setProperty("--ccs-brb-stack-gap", (Number(prop(item, "stackGap", 12)) || 12) + "px");
  el.style.setProperty("--ccs-brb-radius", (Math.max(0, Number(prop(item, "borderRadiusPx", 20)) || 20)) + "px");
  el.style.setProperty("--ccs-brb-color", String(prop(item, "color", "#ffffff") || "#ffffff"));
  el.style.setProperty("--ccs-brb-color2", String(prop(item, "color2", "#ff7a00") || "#ff7a00"));
  el.style.setProperty("--ccs-brb-bg", String(prop(item, "backgroundColor", "rgba(12,12,18,0.82)") || "rgba(12,12,18,0.82)"));
  el.style.setProperty("--ccs-brb-border", String(prop(item, "borderColor", "rgba(255,122,0,0.45)") || "rgba(255,122,0,0.45)"));

  const titleFont = prop(item, "titleFont", null) as Record<string, unknown> | null;
  const messageFont = prop(item, "messageFont", null) as Record<string, unknown> | null;
  if (titleFont) {
    if (titleFont.family) el.style.setProperty("--ccs-brb-title-font", String(titleFont.family));
    if (titleFont.sizePx) el.style.setProperty("--ccs-brb-title-size", Number(titleFont.sizePx) + "px");
    if (titleFont.weight) el.style.setProperty("--ccs-brb-title-weight", String(titleFont.weight));
  }
  if (messageFont) {
    if (messageFont.family) el.style.setProperty("--ccs-brb-message-font", String(messageFont.family));
    if (messageFont.sizePx) el.style.setProperty("--ccs-brb-message-size", Number(messageFont.sizePx) + "px");
    if (messageFont.weight) el.style.setProperty("--ccs-brb-message-weight", String(messageFont.weight));
  }

  el.classList.toggle("ccs-brb-uppercase-title", prop(item, "uppercaseTitle", true) === true);
}

function setIcon(el: HTMLElement, item: LayoutItem): void {
  const iconEl = el.querySelector<HTMLElement>(".ccs-brb-panel-icon");
  if (!iconEl) return;
  const showIcon = prop(item, "showIcon", true) !== false;
  iconEl.style.display = showIcon ? "" : "none";
  const iconUrl = String(prop(item, "iconUrl", "") || "").trim();
  const icon = String(prop(item, "icon", "⏸") || "⏸");
  if (iconUrl) {
    iconEl.innerHTML = `<img src="${iconUrl}" alt="" draggable="false" />`;
    iconEl.classList.add("has-image");
  } else {
    iconEl.textContent = icon;
    iconEl.classList.remove("has-image");
  }
}

export function createBrbPanelEl(item?: LayoutItem): BrbPanelEl {
  const el = document.createElement("div") as BrbPanelEl;
  el.className = "ccs-brb-panel ccs-brb-panel-v-classic";
  el.innerHTML =
    `<div class="ccs-brb-panel-inner">` +
    `<div class="ccs-brb-panel-icon" aria-hidden="true">⏸</div>` +
    `<div class="ccs-brb-panel-stack">` +
    `<div class="ccs-brb-panel-title"></div>` +
    `<div class="ccs-brb-panel-message"></div>` +
    `<div class="ccs-brb-panel-countdown">00:00</div>` +
    `<div class="ccs-brb-panel-progress"><div class="ccs-brb-panel-progress-bar"></div></div>` +
    `</div></div>`;
  if (item) updateBrbPanel(el, item);
  return el;
}

export function updateBrbPanel(
  el: BrbPanelEl,
  item: LayoutItem,
  data?: Record<string, unknown> | null
): void {
  applyBrbPanelAppearance(el, item);
  setIcon(el, item);

  const titleEl = el.querySelector<HTMLElement>(".ccs-brb-panel-title");
  const messageEl = el.querySelector<HTMLElement>(".ccs-brb-panel-message");
  const countdownEl = el.querySelector<HTMLElement>(".ccs-brb-panel-countdown");
  const progressEl = el.querySelector<HTMLElement>(".ccs-brb-panel-progress");

  const showTitle = prop(item, "showTitle", true) !== false;
  const showMessage = prop(item, "showMessage", true) !== false;
  const showCountdown = prop(item, "showCountdown", false) === true;
  const showProgressBar = prop(item, "showProgressBar", false) === true;

  if (titleEl) {
    titleEl.style.display = showTitle ? "" : "none";
    titleEl.textContent = resolveTitle(item);
  }
  if (messageEl) {
    messageEl.style.display = showMessage ? "" : "none";
    messageEl.textContent = String(prop(item, "message", "") || "");
  }
  if (countdownEl) countdownEl.style.display = showCountdown ? "" : "none";
  if (progressEl) progressEl.style.display = showProgressBar && showCountdown ? "" : "none";

  const countdown = ((data && data.countdown) || {}) as Record<string, unknown>;
  el._duration = Math.max(0, Number(countdown.durationSeconds) || Number(countdown.totalSeconds) || 0);
  el._endsAt = countdown.endsAt ? String(countdown.endsAt) : null;

  paintBrbPanel(el, item, data || {});
}

export function paintBrbPanel(
  el: BrbPanelEl,
  item: LayoutItem,
  data: Record<string, unknown> | null | undefined
): void {
  const countdown = ((data && data.countdown) || {}) as Record<string, unknown>;
  const running = countdown.isRunning === true;
  const remaining = resolveCountdownRemaining(data);
  const idle = !running && remaining <= 0;

  el.classList.toggle("is-running", running);
  el.classList.toggle("is-idle", idle);
  el.classList.toggle("is-hidden", idle && prop(item, "hideWhenCountdownIdle", false) === true);

  const countdownEl = el.querySelector<HTMLElement>(".ccs-brb-panel-countdown");
  if (countdownEl && prop(item, "showCountdown", false) === true) {
    countdownEl.textContent = formatCountdownSeconds(remaining, String(prop(item, "countdownFormat", "mm:ss")));
  }

  const bar = el.querySelector<HTMLElement>(".ccs-brb-panel-progress-bar");
  if (bar && prop(item, "showProgressBar", false) === true) {
    const duration = el._duration || Math.max(remaining, 1);
    const pct = duration > 0 ? Math.min(100, ((duration - remaining) / duration) * 100) : 0;
    bar.style.width = `${pct}%`;
  }
}
