import "./styles/index.css";

import { DEFAULT_LAYOUT } from "./defaults/layout";
import { WIDGET_DEFAULTS } from "./defaults/widgets";
import { SHAPE_DEFAULTS, CARD_FRAME_SIZE_PRESETS, CARD_FRAME_VARIANTS } from "./defaults/shapes";
import { SCENE_BG_PRESETS } from "./defaults/scene-bg";
import { uid } from "./utils/format";
import { fetchJson } from "./net/fetch-json";
import { connectWs } from "./net/connect-ws";
import { createRuntime } from "./runtime/create-runtime";
import { registerWidget } from "./extensions/registry";
import { EFFECT_STRATEGIES, registerEffect, listEffectTypes } from "./effects/registry";
import { extUrl } from "./extensions/ext-url";
import { loadExtensions } from "./extensions/loader";

const CcsCanvas = {
  createRuntime,
  fetchJson,
  connectWs,
  WIDGET_DEFAULTS,
  SHAPE_DEFAULTS,
  SCENE_BG_PRESETS,
  CARD_FRAME_SIZE_PRESETS,
  CARD_FRAME_VARIANTS,
  DEFAULT_LAYOUT,
  uid,
  registerWidget,
  registerEffect,
  listEffectTypes,
  EFFECT_STRATEGIES,
  extUrl,
  loadExtensions
};

window.CcsCanvas = CcsCanvas;

export default CcsCanvas;
