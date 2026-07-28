import "./styles/index.css";

import { DEFAULT_LAYOUT } from "./defaults/layout";
import { WIDGET_DEFAULTS } from "./defaults/widgets";
import { SHAPE_DEFAULTS, CARD_FRAME_SIZE_PRESETS, CARD_FRAME_VARIANTS, FRAME_MODES, FRAME_MODE_LABELS } from "./defaults/shapes";
import { SCENE_BG_PRESETS } from "./defaults/scene-bg";
import { MUSIC_VARIANTS, MUSIC_VARIANT_LABELS, MUSIC_SIZE_PRESETS } from "./widgets/music";
import { PARTNER_ROULETTE_TRANSITIONS } from "./widgets/partner-roulette";
import { GOAL_BAR_VARIANTS, GOAL_BAR_SIZE_PRESETS } from "./widgets/goal-bar";
import { EVENT_TICKER_VARIANTS, EVENT_TICKER_SIZE_PRESETS, EVENT_TICKER_SOURCES } from "./widgets/event-ticker";
import { VIEWER_COUNT_VARIANTS, VIEWER_COUNT_SIZE_PRESETS } from "./widgets/viewer-count";
import { LOWER_THIRD_VARIANTS, LOWER_THIRD_SIZE_PRESETS } from "./widgets/lower-third";
import { QR_CODE_VARIANTS, QR_CODE_SIZE_PRESETS } from "./widgets/qr-code";
import { BRB_PANEL_VARIANTS, BRB_PANEL_SIZE_PRESETS, BRB_PANEL_MODES } from "./widgets/brb-panel";
import { ANNOUNCEMENT_BAR_VARIANTS, ANNOUNCEMENT_BAR_SIZE_PRESETS } from "./widgets/announcement-bar";
import {
  ANIMATED_BACKGROUND_VARIANTS,
  ANIMATED_BACKGROUND_SIZE_PRESETS,
  ANIMATED_BACKGROUND_VARIANT_LABELS
} from "./widgets/animated-background";
import { DIVIDER_VARIANTS, DIVIDER_STYLES, DIVIDER_SIZE_PRESETS } from "./shapes/divider";
import { CAM_RING_VARIANTS, CAM_RING_SIZE_PRESETS } from "./shapes/cam-ring";
import { STICKER_PRESETS, STICKER_VARIANTS } from "./shapes/sticker";
import { uid } from "./utils/format";
import { fetchJson } from "./net/fetch-json";
import { connectWs } from "./net/connect-ws";
import { createRuntime } from "./runtime/create-runtime";
import { createItemContent, paintItemContent } from "./runtime/item-content";
import { registerWidget } from "./extensions/registry";
import { EFFECT_STRATEGIES, registerEffect, listEffectTypes } from "./effects/registry";
import { ANIMATION_STRATEGIES, registerAnimation, listAnimationTypes } from "./animations/registry";
import { extUrl } from "./extensions/ext-url";
import { loadExtensions } from "./extensions/loader";

export { connectWs, stopWsReconnect } from "./net/connect-ws";

const CcsCanvas = {
  createRuntime,
  createItemContent,
  paintItemContent,
  fetchJson,
  connectWs,
  WIDGET_DEFAULTS,
  SHAPE_DEFAULTS,
  SCENE_BG_PRESETS,
  CARD_FRAME_SIZE_PRESETS,
  CARD_FRAME_VARIANTS,
  FRAME_MODES,
  FRAME_MODE_LABELS,
  MUSIC_VARIANTS,
  MUSIC_VARIANT_LABELS,
  MUSIC_SIZE_PRESETS,
  PARTNER_ROULETTE_TRANSITIONS,
  GOAL_BAR_VARIANTS,
  GOAL_BAR_SIZE_PRESETS,
  EVENT_TICKER_VARIANTS,
  EVENT_TICKER_SIZE_PRESETS,
  EVENT_TICKER_SOURCES,
  VIEWER_COUNT_VARIANTS,
  VIEWER_COUNT_SIZE_PRESETS,
  LOWER_THIRD_VARIANTS,
  LOWER_THIRD_SIZE_PRESETS,
  QR_CODE_VARIANTS,
  QR_CODE_SIZE_PRESETS,
  BRB_PANEL_VARIANTS,
  BRB_PANEL_SIZE_PRESETS,
  BRB_PANEL_MODES,
  ANNOUNCEMENT_BAR_VARIANTS,
  ANNOUNCEMENT_BAR_SIZE_PRESETS,
  ANIMATED_BACKGROUND_VARIANTS,
  ANIMATED_BACKGROUND_SIZE_PRESETS,
  ANIMATED_BACKGROUND_VARIANT_LABELS,
  DIVIDER_VARIANTS,
  DIVIDER_STYLES,
  DIVIDER_SIZE_PRESETS,
  CAM_RING_VARIANTS,
  CAM_RING_SIZE_PRESETS,
  STICKER_PRESETS,
  STICKER_VARIANTS,
  DEFAULT_LAYOUT,
  uid,
  registerWidget,
  registerEffect,
  listEffectTypes,
  EFFECT_STRATEGIES,
  registerAnimation,
  listAnimationTypes,
  ANIMATION_STRATEGIES,
  extUrl,
  loadExtensions
};

window.CcsCanvas = CcsCanvas;

export default CcsCanvas;
