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

/** Unified border frame modes (legacy types map onto these). */
export const FRAME_MODES = [
  // Legacy / classic
  "rect",
  "circle",
  "corners",
  "bevel",
  "neon",
  "dashed",
  // Creative set
  "double",
  "dotted",
  "groove",
  "ridge",
  "pixel",
  "ticket",
  "stamp",
  "film",
  "hud",
  "hex",
  "octagon",
  "tape",
  "scan",
  "rainbow",
  "comic",
  "frosted",
  "chrome",
  "notch",
  "brackets",
  "orbit"
] as const;

export type FrameMode = (typeof FRAME_MODES)[number];

export const FRAME_MODE_LABELS: Record<FrameMode, string> = {
  rect: "Rechteck",
  circle: "Kreis",
  corners: "Corners",
  bevel: "Bezel",
  neon: "Neon",
  dashed: "Dashed",
  double: "Double",
  dotted: "Dotted",
  groove: "Groove",
  ridge: "Ridge",
  pixel: "Pixel",
  ticket: "Ticket",
  stamp: "Stamp",
  film: "Film",
  hud: "HUD",
  hex: "Hexagon",
  octagon: "Oktagon",
  tape: "Tape",
  scan: "Scan",
  rainbow: "Rainbow",
  comic: "Comic",
  frosted: "Frosted",
  chrome: "Chrome",
  notch: "Notch",
  brackets: "Brackets",
  orbit: "Orbit"
};

/** Old per-style shape ids → mode (still renderable, not listed in palette). */
export const LEGACY_FRAME_TYPE_TO_MODE: Record<string, FrameMode> = {
  "frame.rect": "rect",
  "frame.circle": "circle",
  "frame.corners": "corners",
  "frame.bevel": "bevel",
  "frame.neon": "neon",
  "frame.dashed": "dashed"
};

const legacyFrameDefaults = (mode: FrameMode, size?: { w: number; h: number }): WidgetDefaults => ({
  w: size?.w ?? 400,
  h: size?.h ?? 300,
  props: { mode, color: "#ff7a00", radius: mode === "circle" ? 9999 : 16 }
});

export const SHAPE_DEFAULTS: Record<string, WidgetDefaults> = {
  frame: { w: 400, h: 300, props: { mode: "rect", color: "#ff7a00", radius: 16 } },
  // Legacy aliases for existing layouts / solo URLs
  "frame.rect": legacyFrameDefaults("rect"),
  "frame.circle": legacyFrameDefaults("circle", { w: 320, h: 320 }),
  "frame.corners": legacyFrameDefaults("corners"),
  "frame.bevel": legacyFrameDefaults("bevel", { w: 420, h: 320 }),
  "frame.neon": legacyFrameDefaults("neon"),
  "frame.dashed": legacyFrameDefaults("dashed"),
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
  "shape.cutout": { w: 400, h: 300, props: { radius: 24 } },
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
  },
  "shape.divider": {
    w: 600,
    h: 16,
    props: {
      variant: "line",
      sizePreset: "standard",
      orientation: "h",
      thickness: 2,
      lengthMode: "fill",
      lengthPercent: 80,
      align: "center",
      showCenterMotif: false,
      motif: "diamond",
      motifSize: 18,
      color: "#ff7a00",
      color2: "#ffffff",
      opacity: 1,
      animateShimmer: false
    }
  },
  "shape.cam-ring": {
    w: 320,
    h: 320,
    props: {
      variant: "ring",
      sizePreset: "cam-md",
      strokeWidth: 6,
      gap: 8,
      radius: 999,
      badge: "live",
      badgeText: "LIVE",
      badgePosition: "tr",
      color: "#ff7a00",
      color2: "#ffb36b",
      badgeColor: "#e10600",
      badgeTextColor: "#ffffff",
      pulse: false,
      rotateSlow: false,
      showInnerGlow: true
    }
  },
  "shape.sticker": {
    w: 120,
    h: 120,
    props: {
      variant: "flat",
      preset: "heart",
      src: "",
      fit: "contain",
      rotateDeg: 0,
      scale: 1,
      opacity: 1,
      flipX: false,
      flipY: false,
      color: "#ff7a00",
      color2: "#ffb36b",
      bob: false,
      bobAmplitude: 6,
      bobSpeed: 1,
      spin: false,
      spinSpeed: 1,
      pulse: false,
      pulseAmplitude: 0.08,
      pulseSpeed: 1
    }
  }
};
