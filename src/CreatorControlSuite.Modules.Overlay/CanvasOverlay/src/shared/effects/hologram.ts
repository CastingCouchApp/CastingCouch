import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { setting } from "./setting";

export const hologramStrategy: EffectStrategy = {
  type: "hologram",
  label: "Hologram",
  defaults: { color: "#5ce1ff", opacity: 0.35, speed: 1 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#5ce1ff" },
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.35, step: 0.05, min: 0, max: 1 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const color = String(setting(effect, "color", "#5ce1ff"));
    const opacity = Math.min(1, Math.max(0, Number(setting(effect, "opacity", 0.35))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    layer.className = "ccs-item-fx-layer ccs-item-fx-hologram";
    layer.style.setProperty("--ccs-fx-holo-color", color);
    layer.style.setProperty("--ccs-fx-holo-opacity", String(opacity));
    layer.style.setProperty("--ccs-fx-holo-speed", String(speed));
  }
};
