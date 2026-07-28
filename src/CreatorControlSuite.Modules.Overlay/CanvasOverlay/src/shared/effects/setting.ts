import type { EffectInstance } from "../types";

export function setting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const settings = effect.settings || {};
  if (settings[key] != null) return settings[key];
  if (effect[key] != null) return effect[key];
  return fallback;
}
