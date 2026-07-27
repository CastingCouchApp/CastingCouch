import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import "./particles.css";

function setting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const settings = effect.settings || {};
  if (settings[key] != null) return settings[key];
  if (effect[key] != null) return effect[key];
  return fallback;
}

const MODE_OPTIONS = [
  { value: "ember", label: "Ember (aufsteigend)" },
  { value: "snow", label: "Snow (fallend)" },
  { value: "twinkle", label: "Twinkle (funkeln)" },
  { value: "orbit", label: "Orbit (wirbelnd)" },
  { value: "rain", label: "Rain (Streifen)" },
  { value: "spark", label: "Spark (chaotisch)" },
  { value: "drift", label: "Drift (weich)" },
  { value: "grid", label: "Grid (Raster)" }
];

export const particlesStrategy: EffectStrategy = {
  type: "particles",
  label: "Particles",
  defaults: {
    mode: "ember",
    color: "#ff7a00",
    opacity: 0.55,
    density: 1,
    speed: 1
  },
  fields: [
    { key: "mode", kind: "select", label: "Modus", fallback: "ember", options: MODE_OPTIONS },
    { key: "color", kind: "color", label: "Farbe", fallback: "#ff7a00" },
    { key: "opacity", kind: "number", label: "Opacity", fallback: 0.55, step: 0.05 },
    { key: "density", kind: "number", label: "Dichte", fallback: 1, step: 0.1 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1 }
  ],
  apply(layer, effect, _item: LayoutItem): void {
    const mode = String(setting(effect, "mode", "ember")).toLowerCase();
    const color = String(setting(effect, "color", "#ff7a00"));
    const opacity = Number(setting(effect, "opacity", 0.55));
    const density = Math.max(0.35, Number(setting(effect, "density", setting(effect, "size", 1))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));

    // Legacy: size was pixel grid spacing — map roughly to density if mode missing old size-only packs
    const densityScale = density > 4 ? Math.max(0.4, 18 / density) : density;

    layer.className = "ccs-item-fx-layer ccs-item-fx-particles ccs-item-fx-particles--" + mode;
    layer.style.setProperty("--ccs-fx-particle-color", color);
    layer.style.setProperty("--ccs-fx-particle-opacity", String(opacity));
    layer.style.setProperty("--ccs-fx-particle-density", String(densityScale));
    layer.style.setProperty("--ccs-fx-particle-speed", String(speed));
    layer.style.opacity = String(opacity);
  }
};

export function applyParticlesEffect(layer: HTMLElement, effect: EffectInstance, item: LayoutItem): void {
  particlesStrategy.apply(layer, effect, item);
}
