import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { rgbaFrom } from "../utils/color";
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

export const dropShadowStrategy: EffectStrategy = {
  type: "drop-shadow",
  label: "Drop Shadow",
  targets: ["box", "content"],
  defaults: { color: "#000000", blur: 16, opacity: 0.45, offsetX: 6, offsetY: 10, motion: "off", speed: 1 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#000000" },
    { key: "blur", kind: "number", label: "Blur", fallback: 16, step: 1, min: 0 },
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.45, step: 0.05, min: 0, max: 1 },
    { key: "offsetX", kind: "number", label: "Offset X", fallback: 6, step: 1 },
    { key: "offsetY", kind: "number", label: "Offset Y", fallback: 10, step: 1 },
    { key: "motion", kind: "select", label: "Animation", fallback: "off", options: MOTION_OPTIONS },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer, effect, _item: LayoutItem, host?: HTMLElement): void {
    const color = String(setting(effect, "color", "#000000"));
    const blur = Math.max(0, Number(setting(effect, "blur", 16)));
    const opacity = Math.min(1, Math.max(0, Number(setting(effect, "opacity", 0.45))));
    const offsetX = Number(setting(effect, "offsetX", 6));
    const offsetY = Number(setting(effect, "offsetY", 10));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    const motion = motionOf(effect);
    const rgba = rgbaFrom(color, opacity);
    const shadow = `${offsetX}px ${offsetY}px ${blur}px ${rgba}`;

    // Content mode stays on the host (silhouette). Animation must not switch to box layer.
    if (effect.target === "content") {
      layer.dataset.fxHost = "wrapper";
      layer.className = "ccs-item-fx-layer ccs-item-fx-drop-shadow";
      if (!host) return;
      host.style.setProperty("--ccs-fx-shadow-speed", String(speed));
      host.style.setProperty("--ccs-fx-shadow-i", "1");
      host.style.setProperty("--ccs-fx-shadow-scale", "1");
      host.classList.remove("ccs-item-shadow-content--pulse", "ccs-item-shadow-content--breathe");
      if (motion === "pulse") host.classList.add("ccs-item-shadow-content--pulse");
      if (motion === "breathe") host.classList.add("ccs-item-shadow-content--breathe");
      const filter =
        `drop-shadow(calc(${offsetX}px * var(--ccs-fx-shadow-scale, 1)) ` +
        `calc(${offsetY}px * var(--ccs-fx-shadow-scale, 1)) ` +
        `calc(${blur}px * var(--ccs-fx-shadow-scale, 1)) ` +
        `color-mix(in srgb, ${color} calc(var(--ccs-fx-shadow-i, 1) * ${opacity * 100}%), transparent))`;
      const existing = host.style.filter;
      host.style.filter = existing ? existing + " " + filter : filter;
      return;
    }

    if (motion !== "off") {
      layer.className = "ccs-item-fx-layer ccs-item-fx-drop-shadow ccs-item-fx-drop-shadow--" + motion;
      layer.style.boxShadow = shadow;
      layer.style.setProperty("--ccs-fx-shadow-speed", String(speed));
      layer.style.display = "block";
      return;
    }

    layer.dataset.fxHost = "wrapper";
    layer.className = "ccs-item-fx-layer ccs-item-fx-drop-shadow";
    if (!host) return;
    const existing = host.style.boxShadow;
    host.style.boxShadow = existing ? existing + ", " + shadow : shadow;
  }
};
