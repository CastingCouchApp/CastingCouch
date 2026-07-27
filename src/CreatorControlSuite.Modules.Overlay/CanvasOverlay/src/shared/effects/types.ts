import type { EffectInstance, EffectStrategy, LayoutItem } from "../types";

export type EffectFieldKind = "color" | "number" | "bool" | "text";

export interface EffectField {
  key: string;
  kind: EffectFieldKind;
  label: string;
  fallback?: unknown;
  step?: number;
}

export interface BuiltinEffectStrategy extends EffectStrategy {
  type: string;
  label: string;
  defaults: Record<string, unknown>;
  fields: EffectField[];
}

function settingsOf(effect: EffectInstance): Record<string, unknown> {
  const settings = (effect.settings as Record<string, unknown> | undefined) || {};
  return { ...effect, ...settings };
}

export function readSetting(effect: EffectInstance, key: string, fallback: unknown): unknown {
  const s = settingsOf(effect);
  return s[key] != null ? s[key] : fallback;
}
