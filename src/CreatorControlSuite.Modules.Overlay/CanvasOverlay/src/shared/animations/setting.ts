import type { AnimationInstance } from "../types";

export function animSetting(animation: AnimationInstance, key: string, fallback: unknown): unknown {
  const settings = animation.settings || {};
  if (settings[key] != null) return settings[key];
  if (animation[key] != null) return animation[key];
  return fallback;
}

export function animDuration(animation: AnimationInstance, fallback = 1): number {
  return Math.max(0.15, Number(animSetting(animation, "duration", fallback)));
}

export function animIteration(animation: AnimationInstance): string {
  const loop = animSetting(animation, "loop", true);
  if (loop === false || loop === "false" || loop === 0) return "1";
  return "infinite";
}
