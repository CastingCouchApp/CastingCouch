import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { rgbaFrom } from "../utils/color";
import { setting } from "./setting";

export const neonStrategy: EffectStrategy = {
  type: "neon",
  label: "Neon",
  defaults: { color: "#00f0ff", intensity: 0.85, speed: 1 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#00f0ff" },
    { key: "intensity", kind: "number", label: "Intensität", fallback: 0.85, step: 0.05, min: 0, max: 1 },
    { key: "speed", kind: "number", label: "Pulse-Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const color = String(setting(effect, "color", "#00f0ff"));
    const intensity = Math.min(1, Math.max(0, Number(setting(effect, "intensity", 0.85))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    layer.className = "ccs-item-fx-layer ccs-item-fx-neon";
    layer.style.setProperty("--ccs-fx-neon-color", rgbaFrom(color, intensity));
    layer.style.setProperty("--ccs-fx-neon-soft", rgbaFrom(color, intensity * 0.45));
    layer.style.setProperty("--ccs-fx-neon-speed", String(speed));
  }
};
