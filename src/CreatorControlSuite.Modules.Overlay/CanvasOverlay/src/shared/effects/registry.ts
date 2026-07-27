import type { EffectStrategy } from "../types";
import { glowStrategy } from "./glow";
import { particlesStrategy } from "./particles";
import { scanlinesStrategy } from "./scanlines";
import { vignetteStrategy } from "./vignette";
import { blurStrategy } from "./blur";
import { noiseStrategy } from "./noise";

export const EFFECT_STRATEGIES: Record<string, EffectStrategy> = {
  glow: glowStrategy,
  particles: particlesStrategy,
  scanlines: scanlinesStrategy,
  vignette: vignetteStrategy,
  blur: blurStrategy,
  noise: noiseStrategy
};

export function registerEffect(type: string, strategy: EffectStrategy): void {
  EFFECT_STRATEGIES[type] = strategy;
}

export function listEffectTypes(): string[] {
  return Object.keys(EFFECT_STRATEGIES);
}
