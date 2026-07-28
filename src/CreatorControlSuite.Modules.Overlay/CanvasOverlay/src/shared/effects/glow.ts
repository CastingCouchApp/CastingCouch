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

function contentGlowFilter(color: string, blur: number, intensity: number): string {
  const iCore = intensity * 100;
  const iSoft = intensity * 55;
  const iHalo = intensity * 28;
  const core = Math.max(2, blur * 0.25);
  return [
    `drop-shadow(0 0 calc(${core}px * var(--ccs-fx-glow-scale, 1)) color-mix(in srgb, ${color} calc(var(--ccs-fx-glow-i, 1) * ${iCore}%), transparent))`,
    `drop-shadow(0 0 calc(${blur}px * var(--ccs-fx-glow-scale, 1)) color-mix(in srgb, ${color} calc(var(--ccs-fx-glow-i, 1) * ${iSoft}%), transparent))`,
    `drop-shadow(0 0 calc(${blur * 2}px * var(--ccs-fx-glow-scale, 1)) color-mix(in srgb, ${color} calc(var(--ccs-fx-glow-i, 1) * ${iHalo}%), transparent))`
  ].join(" ");
}

export const glowStrategy: EffectStrategy = {
  type: "glow",
  label: "Glow",
  targets: ["box", "content"],
  defaults: { color: "#ff7a00", blur: 28, intensity: 0.7, motion: "off", speed: 1 },
  fields: [
    { key: "color", kind: "color", label: "Farbe", fallback: "#ff7a00" },
    { key: "blur", kind: "number", label: "Blur", fallback: 28 },
    { key: "intensity", kind: "number", label: "Intensität", fallback: 0.7, step: 0.05 },
    { key: "motion", kind: "select", label: "Animation", fallback: "off", options: MOTION_OPTIONS },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer: HTMLElement, effect: EffectInstance, _item: LayoutItem, host?: HTMLElement): void {
    const color = String(setting(effect, "color", "#ff7a00"));
    const blur = Math.max(0, Number(setting(effect, "blur", setting(effect, "size", 28))));
    const intensity = Math.min(1, Math.max(0, Number(setting(effect, "intensity", setting(effect, "opacity", 0.7)))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    const motion = motionOf(effect);
    const core = rgbaFrom(color, intensity);
    const soft = rgbaFrom(color, intensity * 0.55);
    const halo = rgbaFrom(color, intensity * 0.28);
    const shadow =
      `0 0 ${Math.max(2, blur * 0.25)}px ${core}, ` +
      `0 0 ${blur}px ${soft}, ` +
      `0 0 ${blur * 2}px ${halo}`;

    // Content mode always paints on the host (silhouette). Animation must not switch to box layer.
    if (effect.target === "content") {
      layer.dataset.fxHost = "wrapper";
      if (!host) return;
      host.style.setProperty("--ccs-fx-glow-color", core);
      host.style.setProperty("--ccs-fx-glow-size", blur + "px");
      host.style.setProperty("--ccs-fx-glow-speed", String(speed));
      host.style.setProperty("--ccs-fx-glow-i", "1");
      host.style.setProperty("--ccs-fx-glow-scale", "1");
      host.classList.remove("ccs-item-glow-content--pulse", "ccs-item-glow-content--breathe");
      if (motion === "pulse") host.classList.add("ccs-item-glow-content--pulse");
      if (motion === "breathe") host.classList.add("ccs-item-glow-content--breathe");
      const filters = contentGlowFilter(color, blur, intensity);
      const existing = host.style.filter;
      host.style.filter = existing ? existing + " " + filters : filters;
      return;
    }

    if (motion !== "off") {
      // Box + animation: dedicated layer so opacity/scale pulse without fading content.
      layer.className = "ccs-item-fx-layer ccs-item-fx-glow ccs-item-fx-glow--" + motion;
      layer.style.boxShadow = shadow;
      layer.style.setProperty("--ccs-fx-glow-speed", String(speed));
      layer.style.setProperty("--ccs-fx-glow-color", core);
      layer.style.setProperty("--ccs-fx-glow-size", blur + "px");
      return;
    }

    layer.dataset.fxHost = "wrapper";
    if (!host) return;
    host.style.setProperty("--ccs-fx-glow-color", core);
    host.style.setProperty("--ccs-fx-glow-size", blur + "px");
    const existing = host.style.boxShadow;
    host.style.boxShadow = existing ? existing + ", " + shadow : shadow;
  }
};

export function applyGlowEffect(layer: HTMLElement, effect: EffectInstance, item: LayoutItem): void {
  glowStrategy.apply(layer, effect, item);
}
