import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { rgbaFrom } from "../utils/color";
import { setting } from "./setting";

export const pulseRingStrategy: EffectStrategy = {
  type: "pulse-ring",
  label: "Pulse Ring",
  defaults: { color: "#ff3b6b", intensity: 0.7, speed: 1, count: 2 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#ff3b6b" },
    { key: "intensity", kind: "number", label: "Intensität", fallback: 0.7, step: 0.05, min: 0, max: 1 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 },
    { key: "count", kind: "number", label: "Ringe", fallback: 2, step: 1, min: 1, max: 4 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const color = String(setting(effect, "color", "#ff3b6b"));
    const intensity = Math.min(1, Math.max(0, Number(setting(effect, "intensity", 0.7))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    const count = Math.min(4, Math.max(1, Math.round(Number(setting(effect, "count", 2)))));
    layer.className = "ccs-item-fx-layer ccs-item-fx-pulse-ring";
    layer.dataset.rings = String(count);
    layer.style.setProperty("--ccs-fx-pulse-color", rgbaFrom(color, intensity));
    layer.style.setProperty("--ccs-fx-pulse-speed", String(speed));
    layer.innerHTML = "";
    for (let i = 0; i < count; i++) {
      const ring = document.createElement("span");
      ring.className = "ccs-item-fx-pulse-ring__ring";
      ring.style.animationDelay = `${(i * (1.1 / count)).toFixed(2)}s`;
      layer.appendChild(ring);
    }
  }
};
