import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";
import { setting } from "./setting";

export const glitchStrategy: EffectStrategy = {
  type: "glitch",
  label: "Glitch",
  targets: ["box", "content"],
  defaults: { intensity: 0.55, speed: 1 },
  fields: [
    { key: "intensity", kind: "number", label: "Intensität", fallback: 0.55, step: 0.05, min: 0, max: 1 },
    { key: "speed", kind: "number", label: "Tempo", fallback: 1, step: 0.1, min: 0.2 }
  ],
  apply(layer, effect, _item: LayoutItem, host?: HTMLElement): void {
    const intensity = Math.min(1, Math.max(0, Number(setting(effect, "intensity", 0.55))));
    const speed = Math.max(0.2, Number(setting(effect, "speed", 1)));
    const shift = `${(2 + intensity * 6).toFixed(1)}px`;
    layer.dataset.fxHost = "wrapper";
    layer.className = "ccs-item-fx-layer ccs-item-fx-glitch";
    if (!host) return;
    host.classList.add("ccs-item-has-glitch");
    host.style.setProperty("--ccs-fx-glitch-intensity", String(intensity));
    host.style.setProperty("--ccs-fx-glitch-speed", String(speed));
    host.style.setProperty("--ccs-fx-glitch-shift", shift);
    const filters = [
      `drop-shadow(calc(${shift} * -1) 0 #ff2bd6)`,
      `drop-shadow(${shift} 0 #2bfff2)`
    ].join(" ");
    const existing = host.style.filter;
    host.style.filter = existing ? existing + " " + filters : filters;
  }
};
