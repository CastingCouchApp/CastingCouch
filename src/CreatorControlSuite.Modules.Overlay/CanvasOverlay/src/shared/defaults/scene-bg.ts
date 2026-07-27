export const SCENE_BG_PRESETS: Record<string, Record<string, unknown>> = {
  ember: {
    label: "Ember",
    bgBase: "#030303", bgMid: "#101010", bgDeep: "#1a0d03",
    glow1: "#ff7a00", glow2: "#ffb36b",
    glow1Opacity: 0.18, glow2Opacity: 0.10,
    stripeColor: "#ff7a00", stripeOpacity: 0.065,
    particleColor: "#ff7a00", particleOpacity: 0.34,
    driftDuration: 18, particleDuration: 22,
    vignetteOpacity: 0, scanOpacity: 0
  },
  crimson: {
    label: "Crimson",
    bgBase: "#050102", bgMid: "#14060a", bgDeep: "#2a0610",
    glow1: "#ff2d55", glow2: "#ff8aa8",
    glow1Opacity: 0.22, glow2Opacity: 0.12,
    stripeColor: "#ff2d55", stripeOpacity: 0.08,
    particleColor: "#ff4d6d", particleOpacity: 0.38,
    driftDuration: 16, particleDuration: 20,
    vignetteOpacity: 0.35, scanOpacity: 0.04
  },
  aurora: {
    label: "Aurora",
    bgBase: "#02080a", bgMid: "#061418", bgDeep: "#04302a",
    glow1: "#00e5c0", glow2: "#5cf0ff",
    glow1Opacity: 0.20, glow2Opacity: 0.14,
    stripeColor: "#00c9a7", stripeOpacity: 0.07,
    particleColor: "#7ef9ff", particleOpacity: 0.32,
    driftDuration: 24, particleDuration: 28,
    glow1X: "22%", glow1Y: "30%", glow2X: "78%", glow2Y: "62%",
    vignetteOpacity: 0.25, scanOpacity: 0.05
  },
  violet: {
    label: "Violet Storm",
    bgBase: "#06040f", bgMid: "#120a22", bgDeep: "#1d0b3a",
    glow1: "#a855f7", glow2: "#e9b3ff",
    glow1Opacity: 0.24, glow2Opacity: 0.12,
    stripeColor: "#c084fc", stripeOpacity: 0.075,
    particleColor: "#d8b4fe", particleOpacity: 0.36,
    driftDuration: 20, particleDuration: 24,
    stripeAngle: "128deg", vignetteOpacity: 0.3, scanOpacity: 0.06
  },
  gold: {
    label: "Gold Rush",
    bgBase: "#070501", bgMid: "#151008", bgDeep: "#2a1a05",
    glow1: "#f5b400", glow2: "#ffe08a",
    glow1Opacity: 0.20, glow2Opacity: 0.14,
    stripeColor: "#f0b429", stripeOpacity: 0.07,
    particleColor: "#ffd56a", particleOpacity: 0.30,
    driftDuration: 22, particleDuration: 26,
    vignetteOpacity: 0.2, scanOpacity: 0.03
  },
  ice: {
    label: "Ice Blue",
    bgBase: "#03060c", bgMid: "#0a1422", bgDeep: "#0d2240",
    glow1: "#4ea1ff", glow2: "#b7dcff",
    glow1Opacity: 0.18, glow2Opacity: 0.12,
    stripeColor: "#6eb6ff", stripeOpacity: 0.06,
    particleColor: "#9fd0ff", particleOpacity: 0.28,
    driftDuration: 28, particleDuration: 32,
    particleSize: "88px", vignetteOpacity: 0.28, scanOpacity: 0.05
  },
  lime: {
    label: "Neon Lime",
    bgBase: "#030805", bgMid: "#08140a", bgDeep: "#0f2a12",
    glow1: "#8dff2e", glow2: "#d4ff8a",
    glow1Opacity: 0.18, glow2Opacity: 0.10,
    stripeColor: "#9dff4a", stripeOpacity: 0.07,
    particleColor: "#b8ff66", particleOpacity: 0.33,
    driftDuration: 14, particleDuration: 18,
    stripeAngle: "100deg", vignetteOpacity: 0.22, scanOpacity: 0.04
  },
  magenta: {
    label: "Magenta Pulse",
    bgBase: "#0a0308", bgMid: "#1a0616", bgDeep: "#320a28",
    glow1: "#ff2bd6", glow2: "#ff9ae8",
    glow1Opacity: 0.22, glow2Opacity: 0.13,
    stripeColor: "#ff4de1", stripeOpacity: 0.08,
    particleColor: "#ff7aec", particleOpacity: 0.40,
    driftDuration: 12, particleDuration: 15,
    particleSize: "60px", vignetteOpacity: 0.32, scanOpacity: 0.08
  },
  steel: {
    label: "Steel",
    bgBase: "#050607", bgMid: "#101418", bgDeep: "#1a222b",
    glow1: "#9aa7b5", glow2: "#d7dee6",
    glow1Opacity: 0.14, glow2Opacity: 0.08,
    stripeColor: "#aeb8c4", stripeOpacity: 0.05,
    particleColor: "#c5ced8", particleOpacity: 0.22,
    driftDuration: 36, particleDuration: 42,
    particleSize: "96px", vignetteOpacity: 0.4, scanOpacity: 0.02,
    brightness: 1.05, sat: 0.85
  },
  inferno: {
    label: "Inferno",
    bgBase: "#080200", bgMid: "#1a0800", bgDeep: "#3a1000",
    glow1: "#ff4500", glow2: "#ffb347",
    glow1Opacity: 0.28, glow2Opacity: 0.16,
    stripeColor: "#ff5a1a", stripeOpacity: 0.10,
    particleColor: "#ff7a33", particleOpacity: 0.42,
    driftDuration: 10, particleDuration: 12,
    glow1Size: "36%", glow2Size: "40%",
    vignetteOpacity: 0.38, scanOpacity: 0.10,
    brightness: 1.08, sat: 1.15
  }
};
