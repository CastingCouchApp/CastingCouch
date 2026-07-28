import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { setting } from "./setting";

export const rainbowStrategy: EffectStrategy = {
  type: "rainbow",
  label: "Rainbow",
  defaults: { width: 4, speed: 1, opacity: 0.95 },
  fields: [
    { key: "width", kind: "number", label: "Dicke px", fallback: 4, step: 0.5, min: 1 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 },
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.95, step: 0.05, min: 0, max: 1 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const width = Math.max(1, Number(setting(effect, "width", 4)));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    const opacity = Math.min(1, Math.max(0, Number(setting(effect, "opacity", 0.95))));
    layer.className = "ccs-item-fx-layer ccs-item-fx-rainbow";
    layer.style.setProperty("--ccs-fx-rainbow-width", width + "px");
    layer.style.setProperty("--ccs-fx-rainbow-speed", String(speed));
    layer.style.opacity = String(opacity);
  }
};
