import type { AnimationStrategy, LayoutItem } from "../types";
import { animDuration, animIteration, animSetting } from "./setting";

const LOOP_FIELD = {
  key: "loop",
  kind: "bool" as const,
  label: "Loop"
};

const DURATION_FIELD = {
  key: "duration",
  kind: "number" as const,
  label: "Dauer s",
  fallback: 1,
  step: 0.1,
  min: 0.15
};

export const fadeStrategy: AnimationStrategy = {
  type: "fade",
  label: "Fade",
  defaults: { duration: 1.4, loop: true, opacityMin: 0.25 },
  fields: [
    DURATION_FIELD,
    LOOP_FIELD,
    { key: "opacityMin", kind: "number", label: "Min Opacity", fallback: 0.25, step: 0.05, min: 0, max: 1 }
  ],
  apply(target, animation) {
    const min = Math.min(1, Math.max(0, Number(animSetting(animation, "opacityMin", 0.25))));
    target.style.setProperty("--ccs-anim-fade-min", String(min));
    target.classList.add("ccs-item-anim-fade");
    return `ccs-anim-fade ${animDuration(animation, 1.4)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const slideStrategy: AnimationStrategy = {
  type: "slide",
  label: "Slide",
  defaults: { duration: 1.2, loop: true, direction: "up", distance: 24 },
  fields: [
    DURATION_FIELD,
    LOOP_FIELD,
    {
      key: "direction",
      kind: "select",
      label: "Richtung",
      fallback: "up",
      options: [
        { value: "up", label: "Hoch" },
        { value: "down", label: "Runter" },
        { value: "left", label: "Links" },
        { value: "right", label: "Rechts" }
      ]
    },
    { key: "distance", kind: "number", label: "Distanz px", fallback: 24, step: 1, min: 4 }
  ],
  apply(target, animation) {
    const direction = String(animSetting(animation, "direction", "up"));
    const distance = Math.max(4, Number(animSetting(animation, "distance", 24)));
    let x = "0px";
    let y = "0px";
    if (direction === "up") y = `${distance}px`;
    else if (direction === "down") y = `-${distance}px`;
    else if (direction === "left") x = `${distance}px`;
    else if (direction === "right") x = `-${distance}px`;
    target.style.setProperty("--ccs-anim-slide-x", x);
    target.style.setProperty("--ccs-anim-slide-y", y);
    target.classList.add("ccs-item-anim-slide");
    return `ccs-anim-slide ${animDuration(animation, 1.2)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const bounceStrategy: AnimationStrategy = {
  type: "bounce",
  label: "Bounce",
  defaults: { duration: 1.1, loop: true, height: 18 },
  fields: [
    DURATION_FIELD,
    LOOP_FIELD,
    { key: "height", kind: "number", label: "Höhe px", fallback: 18, step: 1, min: 4 }
  ],
  apply(target, animation) {
    target.style.setProperty("--ccs-anim-bounce-h", Math.max(4, Number(animSetting(animation, "height", 18))) + "px");
    target.classList.add("ccs-item-anim-bounce");
    return `ccs-anim-bounce ${animDuration(animation, 1.1)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const popStrategy: AnimationStrategy = {
  type: "pop",
  label: "Pop",
  defaults: { duration: 0.9, loop: true, scale: 1.12 },
  fields: [
    { ...DURATION_FIELD, fallback: 0.9 },
    LOOP_FIELD,
    { key: "scale", kind: "number", label: "Scale", fallback: 1.12, step: 0.02, min: 1.02, max: 1.5 }
  ],
  apply(target, animation) {
    target.style.setProperty("--ccs-anim-pop-scale", String(Math.min(1.5, Math.max(1.02, Number(animSetting(animation, "scale", 1.12))))));
    target.classList.add("ccs-item-anim-pop");
    return `ccs-anim-pop ${animDuration(animation, 0.9)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const shakeStrategy: AnimationStrategy = {
  type: "shake",
  label: "Shake",
  defaults: { duration: 0.55, loop: true, intensity: 6 },
  fields: [
    { ...DURATION_FIELD, fallback: 0.55 },
    LOOP_FIELD,
    { key: "intensity", kind: "number", label: "Intensität", fallback: 6, step: 1, min: 1, max: 24 }
  ],
  apply(target, animation) {
    target.style.setProperty("--ccs-anim-shake", Math.max(1, Number(animSetting(animation, "intensity", 6))) + "px");
    target.classList.add("ccs-item-anim-shake");
    return `ccs-anim-shake ${animDuration(animation, 0.55)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const floatStrategy: AnimationStrategy = {
  type: "float",
  label: "Float",
  defaults: { duration: 2.8, loop: true, amplitude: 10 },
  fields: [
    { ...DURATION_FIELD, fallback: 2.8 },
    LOOP_FIELD,
    { key: "amplitude", kind: "number", label: "Amplitude px", fallback: 10, step: 1, min: 2 }
  ],
  apply(target, animation) {
    target.style.setProperty("--ccs-anim-float", Math.max(2, Number(animSetting(animation, "amplitude", 10))) + "px");
    target.classList.add("ccs-item-anim-float");
    return `ccs-anim-float ${animDuration(animation, 2.8)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const pulseStrategy: AnimationStrategy = {
  type: "pulse",
  label: "Pulse",
  defaults: { duration: 1.3, loop: true, scale: 1.08 },
  fields: [
    { ...DURATION_FIELD, fallback: 1.3 },
    LOOP_FIELD,
    { key: "scale", kind: "number", label: "Scale", fallback: 1.08, step: 0.02, min: 1.02, max: 1.4 }
  ],
  apply(target, animation) {
    target.style.setProperty("--ccs-anim-pulse-scale", String(Math.min(1.4, Math.max(1.02, Number(animSetting(animation, "scale", 1.08))))));
    target.classList.add("ccs-item-anim-pulse");
    return `ccs-anim-pulse ${animDuration(animation, 1.3)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const spinStrategy: AnimationStrategy = {
  type: "spin",
  label: "Spin",
  defaults: { duration: 4, loop: true, direction: "cw" },
  fields: [
    { ...DURATION_FIELD, fallback: 4 },
    LOOP_FIELD,
    {
      key: "direction",
      kind: "select",
      label: "Richtung",
      fallback: "cw",
      options: [
        { value: "cw", label: "Uhrzeigersinn" },
        { value: "ccw", label: "Gegen Uhrzeigersinn" }
      ]
    }
  ],
  apply(target, animation) {
    const dir = String(animSetting(animation, "direction", "cw")) === "ccw" ? "reverse" : "normal";
    target.classList.add("ccs-item-anim-spin");
    return `ccs-anim-spin ${animDuration(animation, 4)}s linear ${animIteration(animation)} ${dir}`;
  }
};

export const wiggleStrategy: AnimationStrategy = {
  type: "wiggle",
  label: "Wiggle",
  defaults: { duration: 0.7, loop: true, angle: 6 },
  fields: [
    { ...DURATION_FIELD, fallback: 0.7 },
    LOOP_FIELD,
    { key: "angle", kind: "number", label: "Winkel °", fallback: 6, step: 0.5, min: 1, max: 25 }
  ],
  apply(target, animation) {
    target.style.setProperty("--ccs-anim-wiggle", Math.max(1, Number(animSetting(animation, "angle", 6))) + "deg");
    target.classList.add("ccs-item-anim-wiggle");
    return `ccs-anim-wiggle ${animDuration(animation, 0.7)}s ease-in-out ${animIteration(animation)}`;
  }
};

export const flipStrategy: AnimationStrategy = {
  type: "flip",
  label: "Flip",
  defaults: { duration: 1.6, loop: true, axis: "y" },
  fields: [
    { ...DURATION_FIELD, fallback: 1.6 },
    LOOP_FIELD,
    {
      key: "axis",
      kind: "select",
      label: "Achse",
      fallback: "y",
      options: [
        { value: "y", label: "Y (horizontal)" },
        { value: "x", label: "X (vertikal)" }
      ]
    }
  ],
  apply(target, animation, _item: LayoutItem) {
    const axis = String(animSetting(animation, "axis", "y")) === "x" ? "x" : "y";
    target.classList.add("ccs-item-anim-flip", `ccs-item-anim-flip--${axis}`);
    return `ccs-anim-flip-${axis} ${animDuration(animation, 1.6)}s ease-in-out ${animIteration(animation)}`;
  }
};
