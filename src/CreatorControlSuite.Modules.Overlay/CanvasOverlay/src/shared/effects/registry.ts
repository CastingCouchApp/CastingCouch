import type { EffectStrategy } from "../types";
import { glowStrategy } from "./glow";
import { particlesStrategy } from "./particles";
import { scanlinesStrategy } from "./scanlines";
import { vignetteStrategy } from "./vignette";
import { blurStrategy } from "./blur";
import { noiseStrategy } from "./noise";
import { neonStrategy } from "./neon";
import { glitchStrategy } from "./glitch";
import { sparkleStrategy } from "./sparkle";
import { auroraStrategy } from "./aurora";
import { pulseRingStrategy } from "./pulse-ring";
import { hologramStrategy } from "./hologram";
import { outlineStrategy } from "./outline";
import { dropShadowStrategy } from "./drop-shadow";
import { rainbowStrategy } from "./rainbow";
import { spotlightStrategy } from "./spotlight";

export const EFFECT_STRATEGIES: Record<string, EffectStrategy> = {
  glow: glowStrategy,
  particles: particlesStrategy,
  scanlines: scanlinesStrategy,
  vignette: vignetteStrategy,
  blur: blurStrategy,
  noise: noiseStrategy,
  neon: neonStrategy,
  glitch: glitchStrategy,
  sparkle: sparkleStrategy,
  aurora: auroraStrategy,
  "pulse-ring": pulseRingStrategy,
  hologram: hologramStrategy,
  outline: outlineStrategy,
  "drop-shadow": dropShadowStrategy,
  rainbow: rainbowStrategy,
  spotlight: spotlightStrategy
};

export function registerEffect(type: string, strategy: EffectStrategy): void {
  EFFECT_STRATEGIES[type] = strategy;
}

export function listEffectTypes(): string[] {
  return Object.keys(EFFECT_STRATEGIES);
}
