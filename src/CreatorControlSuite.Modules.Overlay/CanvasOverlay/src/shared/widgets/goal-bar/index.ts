import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { applySizeClass, applyVariantClasses, pickVariant } from "../../utils/look";
import "./goal-bar.css";

export const GOAL_BAR_VARIANTS = [
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

export const GOAL_BAR_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  mini: { w: 280, h: 56, label: "Mini" },
  compact: { w: 420, h: 72, label: "Compact" },
  standard: { w: 560, h: 88, label: "Standard" },
  wide: { w: 760, h: 96, label: "Wide" },
  banner: { w: 920, h: 64, label: "Banner" }
};

const SIZE_KEYS = Object.keys(GOAL_BAR_SIZE_PRESETS);

type GoalKind = "followers" | "subs" | "bits" | "custom";

type GoalBarEl = HTMLElement & {
  _lastPct?: number;
};

function goalKind(item: LayoutItem): GoalKind {
  const raw = String(prop(item, "kind", "followers") || "followers").toLowerCase();
  return ["followers", "subs", "bits", "custom"].includes(raw) ? (raw as GoalKind) : "followers";
}

function defaultLabel(kind: GoalKind): string {
  if (kind === "subs") return "Sub Goal";
  if (kind === "bits") return "Bits Goal";
  if (kind === "custom") return "Goal";
  return "Follower Goal";
}

export function resolveGoalBarValues(
  item: LayoutItem,
  data: Record<string, unknown> | null | undefined
): { current: number; target: number; label: string } {
  const kind = goalKind(item);
  const stats = ((data && data.stats) || {}) as Record<string, unknown>;
  const twitch = ((data && data.twitch) || {}) as Record<string, unknown>;
  const stateKey = kind === "subs" ? "subGoalState" : kind === "bits" || kind === "custom" ? "donationGoalState" : "followerGoalState";
  const goalState = (twitch[stateKey] || {}) as Record<string, unknown>;
  const label = String(prop(item, "label", goalState.title || defaultLabel(kind)) || defaultLabel(kind));
  const targetOverride = prop(item, "target", null);
  const currentOverride = prop(item, "current", null);

  let current = 0;
  let target = Math.max(1, Number(targetOverride) || 1);

  if (kind === "followers") {
    current = Number(twitch.followers || 0);
    target = Math.max(1, Number(targetOverride != null ? targetOverride : goalState.target ?? twitch.followerGoal ?? 200));
  } else if (kind === "subs") {
    current = Number(goalState.current ?? (Number(stats.newSubscriptions || 0) + Number(stats.giftSubscriptions || 0)));
    target = Math.max(1, Number(targetOverride != null ? targetOverride : goalState.target ?? 100));
  } else if (kind === "bits") {
    current = Number(goalState.current ?? stats.bitsCheered ?? 0);
    target = Math.max(1, Number(targetOverride != null ? targetOverride : goalState.target ?? 1000));
  } else {
    current = Number(currentOverride ?? goalState.current ?? 0);
    target = Math.max(1, Number(targetOverride != null ? targetOverride : goalState.target ?? 100));
  }

  if (currentOverride != null && currentOverride !== "") {
    current = Number(currentOverride) || 0;
  }

  return { current, target, label };
}

function applyAppearance(el: GoalBarEl, item: LayoutItem): void {
  const variant = pickVariant(prop(item, "variant", "classic"), GOAL_BAR_VARIANTS);
  const sizeKey = pickVariant(prop(item, "sizePreset", "standard"), SIZE_KEYS, "standard");
  applyVariantClasses(el, "ccs-goal-bar-v-", variant, GOAL_BAR_VARIANTS);
  applySizeClass(el, "ccs-goal-bar-s-", sizeKey, SIZE_KEYS);

  const color = String(prop(item, "color", "") || "");
  const color2 = String(prop(item, "color2", "") || "");
  const trackColor = String(prop(item, "trackColor", "") || "");
  const textColor = String(prop(item, "textColor", "") || "");
  const fontFamily = String(prop(item, "fontFamily", "") || "");
  const fontSizePx = Math.max(8, Number(prop(item, "fontSizePx", 0)) || 0);
  const barHeightPx = Math.max(4, Number(prop(item, "barHeightPx", 0)) || 0);
  const borderRadiusPx = Math.max(0, Number(prop(item, "borderRadiusPx", 0)) || 0);
  const fillStyle = String(prop(item, "fillStyle", "solid") || "solid");

  if (color) el.style.setProperty("--ccs-goal-color", color);
  else el.style.removeProperty("--ccs-goal-color");
  if (color2) el.style.setProperty("--ccs-goal-color2", color2);
  else el.style.removeProperty("--ccs-goal-color2");
  if (trackColor) el.style.setProperty("--ccs-goal-track", trackColor);
  else el.style.removeProperty("--ccs-goal-track");
  if (textColor) el.style.setProperty("--ccs-goal-text", textColor);
  else el.style.removeProperty("--ccs-goal-text");
  if (fontFamily) el.style.setProperty("--ccs-goal-font", fontFamily);
  else el.style.removeProperty("--ccs-goal-font");
  if (fontSizePx) el.style.setProperty("--ccs-goal-font-size", fontSizePx + "px");
  else el.style.removeProperty("--ccs-goal-font-size");
  if (barHeightPx) el.style.setProperty("--ccs-goal-bar-h", barHeightPx + "px");
  else el.style.removeProperty("--ccs-goal-bar-h");
  if (borderRadiusPx || borderRadiusPx === 0) {
    el.style.setProperty("--ccs-goal-radius", borderRadiusPx + "px");
  } else {
    el.style.removeProperty("--ccs-goal-radius");
  }

  el.classList.toggle("ccs-goal-fill-gradient", fillStyle === "gradient");
  el.classList.toggle("ccs-goal-fill-striped", fillStyle === "striped");
  el.classList.toggle("ccs-goal-fill-solid", fillStyle !== "gradient" && fillStyle !== "striped");
  el.classList.toggle("ccs-goal-animate", prop(item, "animateFill", true) !== false);
  el.classList.toggle("ccs-goal-pulse", prop(item, "pulseOnProgress", false) === true);
}

export function createGoalBarEl(item?: LayoutItem): GoalBarEl {
  const el = document.createElement("div") as GoalBarEl;
  el.className = "ccs-goal-bar ccs-goal-bar-v-classic ccs-goal-bar-s-standard";
  el.innerHTML =
    `<div class="ccs-goal-bar-label-row">` +
    `<span class="ccs-goal-bar-label"></span>` +
    `</div>` +
    `<div class="ccs-goal-bar-track">` +
    `<div class="ccs-goal-bar-fill"></div>` +
    `</div>` +
    `<div class="ccs-goal-bar-meta">` +
    `<span class="ccs-goal-bar-current"></span>` +
    `<span class="ccs-goal-bar-percent"></span>` +
    `<span class="ccs-goal-bar-remaining"></span>` +
    `<span class="ccs-goal-bar-target"></span>` +
    `</div>`;
  if (item) updateGoalBar(el, item);
  return el;
}

export function updateGoalBar(
  el: GoalBarEl,
  item: LayoutItem,
  data?: Record<string, unknown> | null
): void {
  applyAppearance(el, item);
  const { current, target, label } = resolveGoalBarValues(item, data);
  const pct = Math.min(100, Math.max(0, (current / target) * 100));
  const remaining = Math.max(0, target - current);
  const complete = current >= target;

  const showLabel = prop(item, "showLabel", true) !== false;
  const showCurrent = prop(item, "showCurrent", true) !== false;
  const showTarget = prop(item, "showTarget", true) !== false;
  const showPercent = prop(item, "showPercent", true) !== false;
  const showRemaining = prop(item, "showRemaining", false) === true;
  const hideWhenComplete = prop(item, "hideWhenComplete", false) === true;

  el.classList.toggle("is-complete", complete);
  el.classList.toggle("is-hidden", hideWhenComplete && complete);

  const labelRow = el.querySelector<HTMLElement>(".ccs-goal-bar-label-row");
  const labelEl = el.querySelector<HTMLElement>(".ccs-goal-bar-label");
  const currentEl = el.querySelector<HTMLElement>(".ccs-goal-bar-current");
  const targetEl = el.querySelector<HTMLElement>(".ccs-goal-bar-target");
  const percentEl = el.querySelector<HTMLElement>(".ccs-goal-bar-percent");
  const remainingEl = el.querySelector<HTMLElement>(".ccs-goal-bar-remaining");
  const fill = el.querySelector<HTMLElement>(".ccs-goal-bar-fill");

  if (labelRow) labelRow.style.display = showLabel ? "" : "none";
  if (labelEl) labelEl.textContent = label;
  if (currentEl) {
    currentEl.style.display = showCurrent ? "" : "none";
    currentEl.textContent = String(Math.floor(current));
  }
  if (targetEl) {
    targetEl.style.display = showTarget ? "" : "none";
    targetEl.textContent = showTarget ? `/ ${Math.floor(target)}` : "";
  }
  if (percentEl) {
    percentEl.style.display = showPercent ? "" : "none";
    percentEl.textContent = `${Math.round(pct)}%`;
  }
  if (remainingEl) {
    remainingEl.style.display = showRemaining ? "" : "none";
    remainingEl.textContent = showRemaining ? `${remaining} left` : "";
  }
  if (fill) fill.style.width = `${pct}%`;

  if (el._lastPct != null && pct > el._lastPct) {
    el.classList.add("ccs-goal-progress-bump");
    window.setTimeout(() => el.classList.remove("ccs-goal-progress-bump"), 450);
  }
  el._lastPct = pct;
}
