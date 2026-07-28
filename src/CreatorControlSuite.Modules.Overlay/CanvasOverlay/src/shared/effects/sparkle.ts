import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { setting } from "./setting";

export const sparkleStrategy: EffectStrategy = {
  type: "sparkle",
  label: "Sparkle",
  defaults: { color: "#fff6a8", density: 1, speed: 1, opacity: 0.85 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#fff6a8" },
    { key: "density", kind: "number", label: "Dichte", fallback: 1, step: 0.1, min: 0.3 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 },
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.85, step: 0.05, min: 0, max: 1 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const color = String(setting(effect, "color", "#fff6a8"));
    const density = Math.max(0.3, Number(setting(effect, "density", 1)));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    const opacity = Math.min(1, Math.max(0, Number(setting(effect, "opacity", 0.85))));
    layer.className = "ccs-item-fx-layer ccs-item-fx-sparkle";
    layer.style.setProperty("--ccs-fx-sparkle-color", color);
    layer.style.setProperty("--ccs-fx-sparkle-density", String(density));
    layer.style.setProperty("--ccs-fx-sparkle-speed", String(speed));
    layer.style.opacity = String(opacity);
  }
};
