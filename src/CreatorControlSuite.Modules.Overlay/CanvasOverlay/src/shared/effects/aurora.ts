import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { setting } from "./setting";

export const auroraStrategy: EffectStrategy = {
  type: "aurora",
  label: "Aurora",
  defaults: { color: "#4dffb5", color2: "#7a5cff", opacity: 0.45, speed: 1 },
  fields: [
    { key: "color", kind: "color", label: "Farbe A", fallback: "#4dffb5" },
    { key: "color2", kind: "color", label: "Farbe B", fallback: "#7a5cff" },
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.45, step: 0.05, min: 0, max: 1 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const color = String(setting(effect, "color", "#4dffb5"));
    const color2 = String(setting(effect, "color2", "#7a5cff"));
    const opacity = Math.min(1, Math.max(0, Number(setting(effect, "opacity", 0.45))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    layer.className = "ccs-item-fx-layer ccs-item-fx-aurora";
    layer.style.setProperty("--ccs-fx-aurora-a", color);
    layer.style.setProperty("--ccs-fx-aurora-b", color2);
    layer.style.setProperty("--ccs-fx-aurora-opacity", String(opacity));
    layer.style.setProperty("--ccs-fx-aurora-speed", String(speed));
  }
};
