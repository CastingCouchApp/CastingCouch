import type { WidgetDefaults } from "../types";

export const CARD_FRAME_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  chatting: { w: 1060, h: 420, label: "Just Chatting" },
  square: { w: 500, h: 500, label: "Quadrat" },
  metaschutz: { w: 1060, h: 500, label: "Metaschutz" },
  start: { w: 1060, h: 500, label: "Start" },
  brb: { w: 1060, h: 420, label: "BRB" },
  ending: { w: 920, h: 500, label: "Ending" }
};

export const CARD_FRAME_VARIANTS = [
  "classic", "neon", "soft", "bold", "outline", "glass", "cyber", "minimal"
];

export const SHAPE_DEFAULTS: Record<string, WidgetDefaults> = {
  "frame.rect": { w: 400, h: 300, props: { color: "#ff7a00", radius: 16 } },
  "frame.circle": { w: 320, h: 320, props: { color: "#ff7a00" } },
  "frame.corners": { w: 400, h: 300, props: { color: "#ff7a00" } },
  "frame.bevel": { w: 420, h: 320, props: { color: "#ff7a00" } },
  "frame.neon": { w: 400, h: 300, props: { color: "#ff7a00" } },
  "frame.dashed": { w: 400, h: 300, props: { color: "#ff7a00" } },
  "frame.card": {
    w: 1060,
    h: 500,
    props: {
      variant: "classic",
      sizePreset: "metaschutz",
      color: "#ff7a00",
      color2: "#ffb36b",
      fillOpacity: 0.18,
      showSweep: true,
      showLines: true
    }
  },
  "shape.vignette": { w: 1920, h: 1080, props: {} },
  "shape.scene-bg": {
    w: 1920,
    h: 1080,
    props: {
      preset: "ember",
      speed: 1,
      stripes: true,
      particles: true,
      paused: false
    }
  }
};
