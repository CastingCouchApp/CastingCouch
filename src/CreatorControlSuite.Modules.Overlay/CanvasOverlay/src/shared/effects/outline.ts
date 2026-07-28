import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { setting } from "./setting";

const MOTION_OPTIONS = [
  { value: "off", label: "Aus" },
  { value: "pulse", label: "Pulse" },
  { value: "breathe", label: "Atmen" }
];

function motionOf(effect: EffectInstance): "off" | "pulse" | "breathe" {
  const raw = String(setting(effect, "motion", "off"));
  if (raw === "pulse" || raw === "breathe") return raw;
  if (setting(effect, "animate", false) === true) return "pulse";
  return "off";
}

export const outlineStrategy: EffectStrategy = {
  type: "outline",
  label: "Outline",
  targets: ["box", "content"],
  defaults: { color: "#ffffff", width: 3, opacity: 1, motion: "off", speed: 1 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#ffffff" },
    { key: "width", kind: "number", label: "Dicke px", fallback: 3, step: 0.5, min: 0.5 },
    { key: "opacity", kind: "number", label: "Opacity", fallback: 1, step: 0.05, min: 0, max: 1 },
    { key: "motion", kind: "select", label: "Animation", fallback: "off", options: MOTION_OPTIONS },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer, effect, _item: LayoutItem, host?: HTMLElement): void {
    const color = String(setting(effect, "color", "#ffffff"));
    const width = Math.max(0.5, Number(setting(effect, "width", 3)));
    const opacity = Math.min(1, Math.max(0, Number(setting(effect, "opacity", 1))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    const motion = motionOf(effect);
    layer.dataset.fxHost = "wrapper";
    layer.className = "ccs-item-fx-layer ccs-item-fx-outline";
    if (!host) return;
    host.classList.add("ccs-item-has-outline");
    host.style.setProperty("--ccs-fx-outline-color", color);
    host.style.setProperty("--ccs-fx-outline-width", width + "px");
    host.style.setProperty("--ccs-fx-outline-opacity", String(opacity));
    host.style.setProperty("--ccs-fx-outline-speed", String(speed));
    host.classList.remove("ccs-item-outline--pulse", "ccs-item-outline--breathe");
    if (motion === "pulse") host.classList.add("ccs-item-outline--pulse");
    if (motion === "breathe") host.classList.add("ccs-item-outline--breathe");
  }
};
