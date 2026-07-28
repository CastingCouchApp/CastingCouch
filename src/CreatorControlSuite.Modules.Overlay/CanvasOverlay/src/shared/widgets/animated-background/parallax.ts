import type { LayoutItem } from "../../types";

export const PARALLAX_BACKGROUND_VARIANTS = [
  "mountains",
  "alpine",
  "fuji",
  "mesa",
  "neon-peaks",
  "mist-peaks",
  "lowpoly",
  "papercut",
  "floating",
  "ridge-storm"
] as const;

export type ParallaxBackgroundVariant = (typeof PARALLAX_BACKGROUND_VARIANTS)[number];

export function isParallaxBackgroundVariant(variant: string): variant is ParallaxBackgroundVariant {
  return (PARALLAX_BACKGROUND_VARIANTS as readonly string[]).includes(variant);
}

type Rgb = { r: number; g: number; b: number };

type LayerSpec = {
  depth: number;
  y: number;
  amp: number;
  detail: number;
  seed: number;
  fill: string;
  stroke?: string;
  strokeWidth?: number;
  blur?: number;
  opacity?: number;
  mode?: "ridge" | "fuji" | "mesa" | "lowpoly" | "paper" | "island";
};

type Particle = {
  x: number;
  y: number;
  vx: number;
  vy: number;
  size: number;
  alpha: number;
  kind: "snow" | "star" | "dust" | "ember" | "spark";
};

type ParallaxState = {
  raf: number | null;
  builtKey: string;
  variant: ParallaxBackgroundVariant;
  speed: number;
  paused: boolean;
  intensity: number;
  density: number;
  colors: [string, string, string];
  t0: number;
  layers: HTMLElement[];
  particles: Particle[];
  flash: number;
  lastW: number;
  lastH: number;
};

type AnimatedBgEl = HTMLElement & { _ccsAbgParallax?: ParallaxState };

function clamp(n: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, n));
}

function parseColor(input: string, fallback: Rgb): Rgb {
  const raw = String(input || "").trim();
  if (/^#([0-9a-f]{3})$/i.test(raw)) {
    const h = raw.slice(1);
    return {
      r: parseInt(h[0] + h[0], 16),
      g: parseInt(h[1] + h[1], 16),
      b: parseInt(h[2] + h[2], 16)
    };
  }
  if (/^#([0-9a-f]{6})$/i.test(raw)) {
    return {
      r: parseInt(raw.slice(1, 3), 16),
      g: parseInt(raw.slice(3, 5), 16),
      b: parseInt(raw.slice(5, 7), 16)
    };
  }
  const m = raw.match(/rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)/i);
  if (m) {
    return { r: Number(m[1]), g: Number(m[2]), b: Number(m[3]) };
  }
  return fallback;
}

function rgba(c: Rgb, a: number): string {
  return `rgba(${c.r}, ${c.g}, ${c.b}, ${clamp(a, 0, 1)})`;
}

function mix(a: Rgb, b: Rgb, t: number): Rgb {
  const k = clamp(t, 0, 1);
  return {
    r: Math.round(a.r + (b.r - a.r) * k),
    g: Math.round(a.g + (b.g - a.g) * k),
    b: Math.round(a.b + (b.b - a.b) * k)
  };
}

function hash(n: number): number {
  const x = Math.sin(n * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
}

function ridgePath(
  width: number,
  height: number,
  baseY: number,
  amp: number,
  detail: number,
  seed: number,
  mode: LayerSpec["mode"]
): string {
  const steps = Math.max(8, Math.round(detail));
  const pts: string[] = [`M ${-width * 0.12} ${height}`, `L ${-width * 0.12} ${baseY}`];

  if (mode === "fuji") {
    const peakX = width * 0.52;
    const peakY = baseY - amp * 1.35;
    const left = peakX - width * 0.28;
    const right = peakX + width * 0.3;
    pts.push(`L ${left} ${baseY - amp * 0.15}`);
    pts.push(`L ${peakX - width * 0.04} ${peakY + amp * 0.08}`);
    pts.push(`L ${peakX} ${peakY}`);
    pts.push(`L ${peakX + width * 0.05} ${peakY + amp * 0.1}`);
    pts.push(`L ${right} ${baseY - amp * 0.1}`);
  } else if (mode === "mesa") {
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      const x = t * width * 1.24 - width * 0.12;
      const plateau = hash(seed + i * 3.1) > 0.55;
      const y = plateau
        ? baseY - amp * (0.55 + hash(seed + i) * 0.35)
        : baseY - amp * (0.12 + hash(seed + i * 1.7) * 0.2);
      pts.push(`L ${x.toFixed(1)} ${y.toFixed(1)}`);
      if (plateau && i < steps) {
        const x2 = x + (width * 1.24) / steps * 0.55;
        pts.push(`L ${x2.toFixed(1)} ${y.toFixed(1)}`);
      }
    }
  } else if (mode === "lowpoly") {
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      const x = t * width * 1.24 - width * 0.12;
      const jagged = (i % 2 === 0 ? 1 : 0.35) * amp * (0.4 + hash(seed + i * 2.2) * 0.7);
      const y = baseY - jagged;
      pts.push(`L ${x.toFixed(1)} ${y.toFixed(1)}`);
    }
  } else if (mode === "island") {
    const cx = width * (0.25 + hash(seed) * 0.5);
    const top = baseY - amp;
    const left = cx - width * (0.12 + hash(seed + 1) * 0.1);
    const right = cx + width * (0.14 + hash(seed + 2) * 0.1);
    pts.length = 0;
    pts.push(`M ${left} ${baseY + amp * 0.2}`);
    pts.push(`L ${left + (cx - left) * 0.35} ${top + amp * 0.35}`);
    pts.push(`L ${cx} ${top}`);
    pts.push(`L ${right - (right - cx) * 0.3} ${top + amp * 0.4}`);
    pts.push(`L ${right} ${baseY + amp * 0.15}`);
    pts.push(`Q ${cx} ${baseY + amp * 0.55} ${left} ${baseY + amp * 0.2} Z`);
    return pts.join(" ");
  } else {
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      const x = t * width * 1.24 - width * 0.12;
      const n =
        Math.sin(t * Math.PI * (2.2 + hash(seed) * 2) + seed) * 0.55 +
        Math.sin(t * Math.PI * 7.3 + seed * 1.7) * 0.25 +
        (hash(seed * 10 + i) - 0.5) * 0.35;
      let y = baseY - amp * (0.35 + ((n + 1) / 2) * 0.75);
      if (mode === "paper") {
        y = baseY - amp * (0.4 + hash(seed + i * 1.3) * 0.6);
      }
      pts.push(`L ${x.toFixed(1)} ${y.toFixed(1)}`);
    }
  }

  pts.push(`L ${width * 1.12} ${height}`, `Z`);
  return pts.join(" ");
}

function skyGradient(variant: ParallaxBackgroundVariant, c1: Rgb, c2: Rgb, c3: Rgb): string {
  switch (variant) {
    case "alpine":
      return `linear-gradient(180deg, ${rgba(mix(c2, { r: 180, g: 210, b: 255 }, 0.55), 1)} 0%, ${rgba(c1, 0.9)} 45%, ${rgba(c3, 1)} 100%)`;
    case "fuji":
      return `linear-gradient(180deg, ${rgba(mix(c2, { r: 255, g: 180, b: 120 }, 0.4), 1)} 0%, ${rgba(c1, 0.85)} 40%, ${rgba(c3, 1)} 100%)`;
    case "mesa":
      return `linear-gradient(180deg, ${rgba(mix(c1, { r: 255, g: 200, b: 120 }, 0.5), 1)} 0%, ${rgba(c2, 0.95)} 55%, ${rgba(c3, 1)} 100%)`;
    case "neon-peaks":
      return `linear-gradient(180deg, #050218 0%, ${rgba(c3, 0.85)} 55%, #02010a 100%)`;
    case "mist-peaks":
      return `linear-gradient(180deg, ${rgba(mix(c1, { r: 200, g: 210, b: 220 }, 0.5), 1)} 0%, ${rgba(c2, 0.7)} 60%, ${rgba(c3, 1)} 100%)`;
    case "lowpoly":
      return `linear-gradient(160deg, ${rgba(c1, 1)} 0%, ${rgba(c2, 1)} 50%, ${rgba(c3, 1)} 100%)`;
    case "papercut":
      return `linear-gradient(180deg, ${rgba(mix(c1, { r: 255, g: 250, b: 240 }, 0.65), 1)} 0%, ${rgba(c2, 0.9)} 100%)`;
    case "floating":
      return `radial-gradient(ellipse at 50% 20%, ${rgba(c1, 0.9)} 0%, ${rgba(c2, 0.85)} 45%, ${rgba(c3, 1)} 100%)`;
    case "ridge-storm":
      return `linear-gradient(180deg, #0b1220 0%, ${rgba(c2, 0.8)} 40%, ${rgba(c3, 1)} 100%)`;
    default:
      return `linear-gradient(180deg, ${rgba(c1, 1)} 0%, ${rgba(c2, 0.95)} 48%, ${rgba(c3, 1)} 100%)`;
  }
}

function layerSpecs(
  variant: ParallaxBackgroundVariant,
  c1: Rgb,
  c2: Rgb,
  c3: Rgb,
  density: number
): LayerSpec[] {
  const d = clamp(density, 0.4, 2.5);
  const dark = mix(c3, { r: 10, g: 10, b: 14 }, 0.55);
  const mid = mix(c2, dark, 0.35);
  const far = mix(c1, mid, 0.45);

  if (variant === "fuji") {
    return [
      { depth: 0.12, y: 0.62, amp: 0.12, detail: 10, seed: 2.1, fill: rgba(far, 0.55), mode: "ridge", blur: 1.5 },
      { depth: 0.28, y: 0.58, amp: 0.28, detail: 12, seed: 4.4, fill: rgba(mid, 0.85), mode: "fuji" },
      { depth: 0.55, y: 0.72, amp: 0.18, detail: 14 * d, seed: 7.2, fill: rgba(dark, 0.92), mode: "ridge" },
      { depth: 0.85, y: 0.82, amp: 0.14, detail: 16 * d, seed: 9.1, fill: rgba(mix(dark, c3, 0.2), 1), mode: "ridge" }
    ];
  }
  if (variant === "mesa") {
    return [
      { depth: 0.15, y: 0.55, amp: 0.16, detail: 12, seed: 1.2, fill: rgba(far, 0.55), mode: "mesa" },
      { depth: 0.35, y: 0.62, amp: 0.2, detail: 14, seed: 3.3, fill: rgba(mid, 0.8), mode: "mesa" },
      { depth: 0.6, y: 0.72, amp: 0.22, detail: 16 * d, seed: 5.5, fill: rgba(dark, 0.92), mode: "mesa" },
      { depth: 0.9, y: 0.84, amp: 0.12, detail: 10, seed: 8.8, fill: rgba(mix(dark, c3, 0.25), 1), mode: "ridge" }
    ];
  }
  if (variant === "neon-peaks") {
    return [
      { depth: 0.18, y: 0.58, amp: 0.2, detail: 18 * d, seed: 2.2, fill: rgba(far, 0.35), stroke: rgba(c1, 0.8), strokeWidth: 2, mode: "ridge" },
      { depth: 0.4, y: 0.66, amp: 0.24, detail: 20 * d, seed: 4.6, fill: rgba(mid, 0.55), stroke: rgba(c2, 0.9), strokeWidth: 2, mode: "ridge" },
      { depth: 0.7, y: 0.76, amp: 0.22, detail: 22 * d, seed: 7.7, fill: rgba(dark, 0.85), stroke: rgba(c1, 0.65), strokeWidth: 1.5, mode: "ridge" },
      { depth: 0.95, y: 0.86, amp: 0.14, detail: 16, seed: 11, fill: rgba(mix(dark, { r: 0, g: 0, b: 0 }, 0.4), 1), mode: "ridge" }
    ];
  }
  if (variant === "lowpoly") {
    return [
      { depth: 0.2, y: 0.58, amp: 0.22, detail: 10, seed: 1.5, fill: rgba(far, 0.7), mode: "lowpoly" },
      { depth: 0.45, y: 0.66, amp: 0.26, detail: 12, seed: 3.8, fill: rgba(mid, 0.85), mode: "lowpoly" },
      { depth: 0.75, y: 0.78, amp: 0.2, detail: 14, seed: 6.4, fill: rgba(dark, 0.95), mode: "lowpoly" }
    ];
  }
  if (variant === "papercut") {
    return [
      { depth: 0.15, y: 0.55, amp: 0.18, detail: 11, seed: 2, fill: rgba(far, 0.75), mode: "paper" },
      { depth: 0.35, y: 0.62, amp: 0.2, detail: 12, seed: 4, fill: rgba(mid, 0.88), mode: "paper" },
      { depth: 0.6, y: 0.72, amp: 0.22, detail: 13, seed: 6, fill: rgba(dark, 0.95), mode: "paper" },
      { depth: 0.9, y: 0.84, amp: 0.14, detail: 10, seed: 8, fill: rgba(mix(dark, c3, 0.15), 1), mode: "paper" }
    ];
  }
  if (variant === "floating") {
    return [
      { depth: 0.2, y: 0.35, amp: 0.1, detail: 8, seed: 1.1, fill: rgba(far, 0.75), mode: "island", opacity: 0.85 },
      { depth: 0.4, y: 0.5, amp: 0.14, detail: 8, seed: 2.7, fill: rgba(mid, 0.88), mode: "island" },
      { depth: 0.65, y: 0.62, amp: 0.16, detail: 8, seed: 4.9, fill: rgba(dark, 0.95), mode: "island" },
      { depth: 0.85, y: 0.78, amp: 0.12, detail: 8, seed: 7.3, fill: rgba(mix(dark, c3, 0.2), 1), mode: "island" }
    ];
  }
  if (variant === "alpine") {
    return [
      { depth: 0.15, y: 0.56, amp: 0.2, detail: 16 * d, seed: 1.4, fill: rgba(mix(far, { r: 230, g: 240, b: 255 }, 0.35), 0.7), mode: "ridge", blur: 1 },
      { depth: 0.38, y: 0.64, amp: 0.26, detail: 18 * d, seed: 3.6, fill: rgba(mix(mid, { r: 220, g: 230, b: 245 }, 0.25), 0.88), mode: "ridge" },
      { depth: 0.68, y: 0.76, amp: 0.22, detail: 20 * d, seed: 6.2, fill: rgba(dark, 0.95), mode: "ridge" },
      { depth: 0.92, y: 0.88, amp: 0.12, detail: 14, seed: 9.5, fill: rgba(mix(dark, c3, 0.2), 1), mode: "ridge" }
    ];
  }
  if (variant === "mist-peaks") {
    return [
      { depth: 0.12, y: 0.58, amp: 0.14, detail: 14, seed: 2.3, fill: rgba(far, 0.35), mode: "ridge", blur: 3 },
      { depth: 0.32, y: 0.66, amp: 0.2, detail: 16 * d, seed: 4.1, fill: rgba(mid, 0.55), mode: "ridge", blur: 2 },
      { depth: 0.58, y: 0.74, amp: 0.22, detail: 18 * d, seed: 6.8, fill: rgba(dark, 0.75), mode: "ridge", blur: 1 },
      { depth: 0.88, y: 0.86, amp: 0.14, detail: 14, seed: 9.9, fill: rgba(mix(dark, c3, 0.15), 0.95), mode: "ridge" }
    ];
  }
  if (variant === "ridge-storm") {
    return [
      { depth: 0.18, y: 0.6, amp: 0.18, detail: 16 * d, seed: 1.9, fill: rgba(far, 0.45), mode: "ridge", blur: 1.5 },
      { depth: 0.42, y: 0.68, amp: 0.24, detail: 20 * d, seed: 4.2, fill: rgba(mid, 0.75), mode: "ridge" },
      { depth: 0.72, y: 0.78, amp: 0.22, detail: 22 * d, seed: 7.5, fill: rgba(dark, 0.92), mode: "ridge" },
      { depth: 0.95, y: 0.9, amp: 0.12, detail: 12, seed: 10.2, fill: rgba(mix(dark, { r: 0, g: 0, b: 0 }, 0.35), 1), mode: "ridge" }
    ];
  }
  // mountains (default parallax)
  return [
    { depth: 0.14, y: 0.58, amp: 0.16, detail: 14 * d, seed: 1.1, fill: rgba(far, 0.55), mode: "ridge", blur: 1.2 },
    { depth: 0.34, y: 0.64, amp: 0.22, detail: 16 * d, seed: 3.4, fill: rgba(mid, 0.8), mode: "ridge" },
    { depth: 0.62, y: 0.74, amp: 0.24, detail: 18 * d, seed: 6.1, fill: rgba(dark, 0.92), mode: "ridge" },
    { depth: 0.9, y: 0.86, amp: 0.14, detail: 14, seed: 8.7, fill: rgba(mix(dark, c3, 0.18), 1), mode: "ridge" }
  ];
}

function ensureParallaxDom(el: HTMLElement): {
  root: HTMLElement;
  sky: HTMLElement;
  ridges: HTMLElement;
  decor: HTMLElement;
  canvas: HTMLCanvasElement;
} {
  let root = el.querySelector<HTMLElement>(".ccs-abg-parallax");
  if (!root) {
    root = document.createElement("div");
    root.className = "ccs-abg-parallax";
    root.innerHTML =
      `<div class="ccs-abg-sky"></div>` +
      `<div class="ccs-abg-decor"></div>` +
      `<div class="ccs-abg-ridges"></div>` +
      `<canvas class="ccs-abg-particles"></canvas>`;
    el.appendChild(root);
  }
  const sky = root.querySelector<HTMLElement>(".ccs-abg-sky")!;
  const ridges = root.querySelector<HTMLElement>(".ccs-abg-ridges")!;
  const decor = root.querySelector<HTMLElement>(".ccs-abg-decor")!;
  const canvas = root.querySelector<HTMLCanvasElement>(".ccs-abg-particles")!;
  return { root, sky, ridges, decor, canvas };
}

function buildDecor(variant: ParallaxBackgroundVariant, decor: HTMLElement, c1: Rgb, c2: Rgb): void {
  decor.innerHTML = "";
  if (variant === "fuji") {
    const sun = document.createElement("div");
    sun.className = "ccs-abg-sun";
    sun.style.background = `radial-gradient(circle, ${rgba(c1, 1)} 0%, ${rgba(c2, 0.7)} 45%, transparent 70%)`;
    decor.appendChild(sun);
  } else if (variant === "neon-peaks") {
    const glow = document.createElement("div");
    glow.className = "ccs-abg-horizon-glow";
    glow.style.background = `radial-gradient(ellipse at 50% 100%, ${rgba(c1, 0.55)}, transparent 60%)`;
    decor.appendChild(glow);
  } else if (variant === "ridge-storm") {
    const clouds = document.createElement("div");
    clouds.className = "ccs-abg-storm-clouds";
    decor.appendChild(clouds);
    const bolt = document.createElement("div");
    bolt.className = "ccs-abg-lightning";
    decor.appendChild(bolt);
  } else if (variant === "mist-peaks") {
    for (let i = 0; i < 3; i++) {
      const fog = document.createElement("div");
      fog.className = "ccs-abg-fog";
      fog.style.setProperty("--ccs-abg-fog-i", String(i));
      fog.style.opacity = String(0.25 + i * 0.12);
      decor.appendChild(fog);
    }
  } else if (variant === "floating") {
    const ring = document.createElement("div");
    ring.className = "ccs-abg-orbit";
    ring.style.borderColor = rgba(c1, 0.35);
    decor.appendChild(ring);
  }
}

function spawnParticles(
  variant: ParallaxBackgroundVariant,
  w: number,
  h: number,
  density: number
): Particle[] {
  const count = Math.round((variant === "alpine" ? 55 : variant === "ridge-storm" ? 40 : 28) * density);
  const out: Particle[] = [];
  for (let i = 0; i < count; i++) {
    const kind: Particle["kind"] =
      variant === "alpine"
        ? "snow"
        : variant === "neon-peaks"
          ? "spark"
          : variant === "ridge-storm"
            ? hash(i * 1.7) > 0.7
              ? "ember"
              : "dust"
            : variant === "floating"
              ? "star"
              : hash(i) > 0.5
                ? "star"
                : "dust";
    out.push({
      x: hash(i * 3.1) * w,
      y: hash(i * 5.7) * h,
      vx: (hash(i * 9.2) - 0.5) * (kind === "snow" ? 0.35 : 0.15),
      vy: kind === "snow" ? 0.35 + hash(i * 2.2) * 0.7 : kind === "ember" ? -0.2 - hash(i) * 0.4 : (hash(i * 4.4) - 0.5) * 0.1,
      size: kind === "star" ? 1 + hash(i * 6) * 1.8 : kind === "snow" ? 1.2 + hash(i) * 2.4 : 1 + hash(i) * 1.5,
      alpha: 0.25 + hash(i * 8.1) * 0.65,
      kind
    });
  }
  return out;
}

function buildScene(
  el: AnimatedBgEl,
  variant: ParallaxBackgroundVariant,
  colors: [string, string, string],
  density: number
): void {
  const { root, sky, ridges, decor, canvas } = ensureParallaxDom(el);
  root.hidden = false;
  const rect = el.getBoundingClientRect();
  const w = Math.max(2, Math.round(rect.width || el.clientWidth || 1920));
  const h = Math.max(2, Math.round(rect.height || el.clientHeight || 1080));

  const c1 = parseColor(colors[0], { r: 255, g: 140, b: 80 });
  const c2 = parseColor(colors[1], { r: 120, g: 80, b: 160 });
  const c3 = parseColor(colors[2], { r: 20, g: 24, b: 40 });

  sky.style.background = skyGradient(variant, c1, c2, c3);
  buildDecor(variant, decor, c1, c2);

  const specs = layerSpecs(variant, c1, c2, c3, density);
  ridges.innerHTML = "";
  const layerEls: HTMLElement[] = [];

  specs.forEach((spec, index) => {
    const wrap = document.createElement("div");
    wrap.className = "ccs-abg-ridge";
    wrap.dataset.depth = String(spec.depth);
    wrap.style.zIndex = String(10 + index);
    if (spec.blur) wrap.style.filter = `blur(${spec.blur}px)`;
    if (spec.opacity != null) wrap.style.opacity = String(spec.opacity);

    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", `0 0 ${w} ${h}`);
    svg.setAttribute("preserveAspectRatio", "none");
    svg.setAttribute("width", "120%");
    svg.setAttribute("height", "100%");
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute(
      "d",
      ridgePath(w, h, h * spec.y, h * spec.amp, Math.round(spec.detail), spec.seed, spec.mode || "ridge")
    );
    path.setAttribute("fill", spec.fill);
    if (spec.stroke) {
      path.setAttribute("stroke", spec.stroke);
      path.setAttribute("stroke-width", String(spec.strokeWidth || 1.5));
    }
    svg.appendChild(path);
    wrap.appendChild(svg);
    ridges.appendChild(wrap);
    layerEls.push(wrap);
  });

  const dpr = Math.min(2, window.devicePixelRatio || 1);
  canvas.width = Math.round(w * dpr);
  canvas.height = Math.round(h * dpr);
  canvas.style.width = "100%";
  canvas.style.height = "100%";

  const state = el._ccsAbgParallax!;
  state.layers = layerEls;
  state.particles = spawnParticles(variant, w, h, density);
  state.lastW = w;
  state.lastH = h;
  state.flash = 0;
  state.builtKey = `${variant}|${colors.join(",")}|${density.toFixed(2)}|${w}x${h}`;
}

function paintFrame(el: AnimatedBgEl, now: number): void {
  const state = el._ccsAbgParallax;
  if (!state) return;

  const speed = state.speed;
  const t = ((now - state.t0) / 1000) * speed;

  if (!state.paused) {
    state.layers.forEach((layer, i) => {
      const depth = Number(layer.dataset.depth) || (i + 1) * 0.2;
      const x = Math.sin(t * (0.22 + depth * 0.35) + i) * (10 + depth * 28);
      const y = Math.cos(t * (0.15 + depth * 0.2) + i * 0.7) * (2 + depth * 6);
      const bob =
        state.variant === "floating"
          ? Math.sin(t * (0.6 + depth) + i * 1.3) * (6 + depth * 10)
          : 0;
      layer.style.transform = `translate3d(${x.toFixed(2)}px, ${(y + bob).toFixed(2)}px, 0)`;
    });

    const sun = el.querySelector<HTMLElement>(".ccs-abg-sun");
    if (sun) {
      const sx = Math.sin(t * 0.15) * 18;
      const sy = Math.cos(t * 0.12) * 10;
      sun.style.transform = `translate3d(${sx}px, ${sy}px, 0)`;
    }

    const fogs = el.querySelectorAll<HTMLElement>(".ccs-abg-fog");
    fogs.forEach((fog, i) => {
      const fx = Math.sin(t * (0.1 + i * 0.05) + i) * (20 + i * 12);
      fog.style.transform = `translate3d(${fx}px, 0, 0)`;
    });

    if (state.variant === "ridge-storm") {
      if (state.flash <= 0 && Math.random() < 0.01 * speed) {
        state.flash = 0.35 + Math.random() * 0.4;
      }
      if (state.flash > 0) {
        state.flash -= 0.04 * speed;
      }
      const bolt = el.querySelector<HTMLElement>(".ccs-abg-lightning");
      if (bolt) {
        bolt.style.opacity = state.flash > 0 ? String(clamp(state.flash, 0, 1)) : "0";
      }
    }
  }

  const canvas = el.querySelector<HTMLCanvasElement>(".ccs-abg-particles");
  if (canvas) {
    const ctx = canvas.getContext("2d");
    if (ctx) {
      const dpr = canvas.width / Math.max(1, state.lastW);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, state.lastW, state.lastH);
      const c1 = parseColor(state.colors[0], { r: 255, g: 255, b: 255 });
      const c2 = parseColor(state.colors[1], { r: 180, g: 220, b: 255 });

      if (!state.paused) {
        for (const p of state.particles) {
          p.x += p.vx * speed * (0.8 + state.intensity);
          p.y += p.vy * speed * (0.8 + state.intensity);
          if (p.y > state.lastH + 8) {
            p.y = -8;
            p.x = hash(p.x + t) * state.lastW;
          }
          if (p.y < -10) {
            p.y = state.lastH + 6;
            p.x = hash(p.y + t) * state.lastW;
          }
          if (p.x < -10) p.x = state.lastW + 6;
          if (p.x > state.lastW + 10) p.x = -6;
        }
      }

      for (const p of state.particles) {
        const alpha = p.alpha * state.intensity;
        if (p.kind === "star") {
          ctx.fillStyle = rgba(c2, alpha);
          ctx.fillRect(p.x, p.y, p.size, p.size);
        } else if (p.kind === "snow") {
          ctx.beginPath();
          ctx.fillStyle = rgba({ r: 245, g: 250, b: 255 }, alpha);
          ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
          ctx.fill();
        } else if (p.kind === "spark") {
          ctx.fillStyle = rgba(c1, alpha);
          ctx.shadowBlur = 6;
          ctx.shadowColor = rgba(c1, 0.8);
          ctx.fillRect(p.x, p.y, p.size, p.size);
          ctx.shadowBlur = 0;
        } else if (p.kind === "ember") {
          ctx.fillStyle = rgba(c1, alpha * 0.85);
          ctx.beginPath();
          ctx.arc(p.x, p.y, p.size * 0.8, 0, Math.PI * 2);
          ctx.fill();
        } else {
          ctx.fillStyle = rgba(c2, alpha * 0.45);
          ctx.fillRect(p.x, p.y, p.size * 0.8, p.size * 0.8);
        }
      }

      if (state.variant === "ridge-storm" && state.flash > 0) {
        ctx.fillStyle = `rgba(220, 230, 255, ${state.flash * 0.25})`;
        ctx.fillRect(0, 0, state.lastW, state.lastH);
      }
    }
  }

  state.raf = requestAnimationFrame((ts) => paintFrame(el, ts));
}

function stopParallax(el: AnimatedBgEl): void {
  const state = el._ccsAbgParallax;
  if (!state) return;
  if (state.raf != null) {
    cancelAnimationFrame(state.raf);
    state.raf = null;
  }
}

export function teardownParallaxBackground(el: HTMLElement): void {
  const node = el as AnimatedBgEl;
  stopParallax(node);
  const root = node.querySelector<HTMLElement>(".ccs-abg-parallax");
  if (root) root.hidden = true;
  node.classList.remove("is-parallax");
}

export function syncParallaxBackground(
  el: HTMLElement,
  _item: LayoutItem,
  variant: ParallaxBackgroundVariant,
  opts: {
    color: string;
    color2: string;
    color3: string;
    speed: number;
    intensity: number;
    density: number;
    paused: boolean;
  }
): void {
  const node = el as AnimatedBgEl;
  node.classList.add("is-parallax");

  if (!node._ccsAbgParallax) {
    node._ccsAbgParallax = {
      raf: null,
      builtKey: "",
      variant,
      speed: opts.speed,
      paused: opts.paused,
      intensity: opts.intensity,
      density: opts.density,
      colors: [opts.color, opts.color2, opts.color3],
      t0: performance.now(),
      layers: [],
      particles: [],
      flash: 0,
      lastW: 0,
      lastH: 0
    };
  }

  const state = node._ccsAbgParallax;
  state.variant = variant;
  state.speed = opts.speed;
  state.paused = opts.paused;
  state.intensity = opts.intensity;
  state.density = opts.density;
  state.colors = [opts.color, opts.color2, opts.color3];

  const rect = node.getBoundingClientRect();
  const w = Math.max(2, Math.round(rect.width || node.clientWidth || 1920));
  const h = Math.max(2, Math.round(rect.height || node.clientHeight || 1080));
  const key = `${variant}|${opts.color},${opts.color2},${opts.color3}|${opts.density.toFixed(2)}|${w}x${h}`;

  if (state.builtKey !== key) {
    buildScene(node, variant, state.colors, opts.density);
  } else {
    ensureParallaxDom(node).root.hidden = false;
  }

  // Keep CSS vignette on fx layer; hide decorative CSS layers in parallax mode via CSS.

  if (state.raf == null) {
    state.t0 = performance.now();
    state.raf = requestAnimationFrame((ts) => paintFrame(node, ts));
  }

  // Rebuild on size changes without thrashing every frame.
  if (!node.dataset.abgRo) {
    node.dataset.abgRo = "1";
    const ro = new ResizeObserver(() => {
      const st = node._ccsAbgParallax;
      if (!st || !node.classList.contains("is-parallax")) return;
      const r = node.getBoundingClientRect();
      const nw = Math.max(2, Math.round(r.width || node.clientWidth || 1920));
      const nh = Math.max(2, Math.round(r.height || node.clientHeight || 1080));
      if (Math.abs(nw - st.lastW) > 8 || Math.abs(nh - st.lastH) > 8) {
        buildScene(node, st.variant, st.colors, st.density);
      }
    });
    ro.observe(node);
    (node as AnimatedBgEl & { _ccsAbgRo?: ResizeObserver })._ccsAbgRo = ro;
  }
}
