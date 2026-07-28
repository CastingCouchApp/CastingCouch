import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { rgbaFrom } from "../utils/color";
import { setting } from "./setting";

export const spotlightStrategy: EffectStrategy = {
  type: "spotlight",
  label: "Spotlight",
  defaults: { color: "#ffffff", intensity: 0.55, size: 42, speed: 1 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#ffffff" },
    { key: "intensity", kind: "number", label: "Intensität", fallback: 0.55, step: 0.05, min: 0, max: 1 },
    { key: "size", kind: "number", label: "Größe %", fallback: 42, step: 1, min: 10, max: 90 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const color = String(setting(effect, "color", "#ffffff"));
    const intensity = Math.min(1, Math.max(0, Number(setting(effect, "intensity", 0.55))));
    const size = Math.min(90, Math.max(10, Number(setting(effect, "size", 42))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    layer.className = "ccs-item-fx-layer ccs-item-fx-spotlight";
    layer.style.setProperty("--ccs-fx-spot-color", rgbaFrom(color, intensity));
    layer.style.setProperty("--ccs-fx-spot-size", size + "%");
    layer.style.setProperty("--ccs-fx-spot-speed", String(speed));
  }
};
