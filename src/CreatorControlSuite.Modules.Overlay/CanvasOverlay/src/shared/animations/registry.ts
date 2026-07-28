import type { AnimationStrategy } from "../types";
import {
  fadeStrategy,
  slideStrategy,
  bounceStrategy,
  popStrategy,
  shakeStrategy,
  floatStrategy,
  pulseStrategy,
  spinStrategy,
  wiggleStrategy,
  flipStrategy
} from "./strategies";

export const ANIMATION_STRATEGIES: Record<string, AnimationStrategy> = {
  fade: fadeStrategy,
  slide: slideStrategy,
  bounce: bounceStrategy,
  pop: popStrategy,
  shake: shakeStrategy,
  float: floatStrategy,
  pulse: pulseStrategy,
  spin: spinStrategy,
  wiggle: wiggleStrategy,
  flip: flipStrategy
};

export function registerAnimation(type: string, strategy: AnimationStrategy): void {
  ANIMATION_STRATEGIES[type] = strategy;
}

export function listAnimationTypes(): string[] {
  return Object.keys(ANIMATION_STRATEGIES);
}
