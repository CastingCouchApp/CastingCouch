import type { LayoutItem } from "../../types";

export const MATRIX_BACKGROUND_VARIANTS = ["hacker"] as const;

export type MatrixBackgroundVariant = (typeof MATRIX_BACKGROUND_VARIANTS)[number];

export function isMatrixBackgroundVariant(variant: string): variant is MatrixBackgroundVariant {
  return (MATRIX_BACKGROUND_VARIANTS as readonly string[]).includes(variant);
}

/** Half-width katakana + digits + latin — classic Matrix glyph set. */
const MATRIX_GLYPHS =
  "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ" +
  "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
  ":・.=*+-<>¦｜ç";

const GLYPH_COUNT = MATRIX_GLYPHS.length;
const MAX_TRAIL = 14;
const MAX_COLS = 48;

type Drop = {
  y: number;
  speed: number;
  len: number;
  glyphs: string[];
  tick: number;
  mutateEvery: number;
};

type MatrixState = {
  raf: number | null;
  builtKey: string;
  speed: number;
  paused: boolean;
  intensity: number;
  density: number;
  colors: [string, string, string];
  rain: [number, number, number];
  bg: [number, number, number];
  head: [number, number, number];
  drops: Drop[];
  cols: number;
  fontSize: number;
  lastW: number;
  lastH: number;
  trail: number;
  canvas: HTMLCanvasElement | null;
  ctx: CanvasRenderingContext2D | null;
};

type MatrixEl = HTMLElement & {
  _ccsAbgMatrix?: MatrixState;
  _ccsAbgMatrixRo?: ResizeObserver;
};

function clamp(n: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, n));
}

function parseRgb(input: string, fallback: [number, number, number]): [number, number, number] {
  const raw = String(input || "").trim();
  if (/^#([0-9a-f]{3})$/i.test(raw)) {
    const h = raw.slice(1);
    return [parseInt(h[0] + h[0], 16), parseInt(h[1] + h[1], 16), parseInt(h[2] + h[2], 16)];
  }
  if (/^#([0-9a-f]{6})$/i.test(raw)) {
    return [parseInt(raw.slice(1, 3), 16), parseInt(raw.slice(3, 5), 16), parseInt(raw.slice(5, 7), 16)];
  }
  const m = raw.match(/rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)/i);
  if (m) return [Number(m[1]), Number(m[2]), Number(m[3])];
  return fallback;
}

function glyph(): string {
  return MATRIX_GLYPHS[(Math.random() * GLYPH_COUNT) | 0] || "0";
}

function ensureCanvas(el: HTMLElement): HTMLCanvasElement {
  let canvas = el.querySelector<HTMLCanvasElement>(".ccs-abg-matrix");
  if (!canvas) {
    canvas = document.createElement("canvas");
    canvas.className = "ccs-abg-matrix";
    el.appendChild(canvas);
  }
  canvas.hidden = false;
  return canvas;
}

function fillGlyphs(buf: string[], len: number): void {
  for (let g = 0; g < len; g++) buf[g] = glyph();
}

function resetDrop(drop: Drop, rows: number): void {
  drop.y = -(Math.random() * rows * 0.6 + 2);
  drop.speed = 0.45 + Math.random() * 0.85;
  drop.len = 6 + ((Math.random() * 8) | 0);
  drop.tick = 0;
  drop.mutateEvery = 4 + ((Math.random() * 8) | 0);
  fillGlyphs(drop.glyphs, drop.len);
}

/** Grow/reuse pooled drops — never discard glyph buffers. */
function ensureDrops(state: MatrixState, cols: number, rows: number): void {
  const drops = state.drops;
  while (drops.length < cols) {
    const glyphs: string[] = new Array(MAX_TRAIL);
    fillGlyphs(glyphs, MAX_TRAIL);
    const drop: Drop = {
      y: 0,
      speed: 1,
      len: MAX_TRAIL,
      glyphs,
      tick: 0,
      mutateEvery: 4
    };
    resetDrop(drop, rows);
    drops.push(drop);
  }
  // Keep surplus drops in the pool; only use the first `cols`.
  for (let i = 0; i < cols; i++) {
    resetDrop(drops[i], rows);
  }
  state.cols = cols;
}

function rebuild(el: MatrixEl, w: number, h: number): void {
  const state = el._ccsAbgMatrix!;
  const canvas = ensureCanvas(el);
  const density = state.density;

  // Larger glyphs → fewer columns. Density tightens spacing a bit, not tiny text.
  const fontSize = clamp(Math.round(34 - density * 4), 24, 44);
  const gap = fontSize * (1.15 + Math.max(0, 1.2 - density) * 0.35);
  const cols = clamp(Math.ceil(w / gap), 6, MAX_COLS);
  const rows = Math.max(6, Math.ceil(h / fontSize));

  // Cap DPR — matrix reads fine at 1x and is much cheaper in OBS/CEF.
  const dpr = 1;
  if (canvas.width !== w || canvas.height !== h) {
    canvas.width = w;
    canvas.height = h;
  }
  canvas.style.width = "100%";
  canvas.style.height = "100%";

  const ctx = canvas.getContext("2d", { alpha: false });
  state.canvas = canvas;
  state.ctx = ctx;
  state.fontSize = fontSize;
  state.lastW = w;
  state.lastH = h;
  state.trail = clamp(0.1 + (1 - state.intensity) * 0.12, 0.08, 0.22);
  ensureDrops(state, cols, rows);
  state.builtKey = `${state.colors.join(",")}|${density.toFixed(2)}|${w}x${h}|${fontSize}`;

  if (ctx) {
    const [br, bg, bb] = state.bg;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = `rgb(${br},${bg},${bb})`;
    ctx.fillRect(0, 0, w, h);
    ctx.font = `600 ${fontSize}px "Courier New", ui-monospace, monospace`;
    ctx.textBaseline = "top";
  }
}

function paint(el: MatrixEl): void {
  const state = el._ccsAbgMatrix;
  if (!state) return;

  const ctx = state.ctx || (state.canvas && state.canvas.getContext("2d"));
  if (!ctx || !state.canvas) {
    state.raf = requestAnimationFrame(() => paint(el));
    return;
  }
  state.ctx = ctx;

  const [cr, cg, cb] = state.rain;
  const [br, bg, bb] = state.bg;
  const [hr, hg, hb] = state.head;
  const w = state.lastW;
  const h = state.lastH;
  const fs = state.fontSize;
  const rows = ((h / fs) | 0) + 2;
  const cols = state.cols;
  const drops = state.drops;
  const gap = w / cols;

  ctx.fillStyle = `rgba(${br},${bg},${bb},${state.trail})`;
  ctx.fillRect(0, 0, w, h);
  ctx.font = `600 ${fs}px "Courier New", ui-monospace, monospace`;

  const headStyle = `rgba(${hr},${hg},${hb},${(0.95 * state.intensity).toFixed(3)})`;
  const step = state.paused ? 0 : state.speed * (0.9 + state.intensity * 0.35);

  for (let i = 0; i < cols; i++) {
    const drop = drops[i];
    if (!drop) continue;

    if (step > 0) {
      drop.tick += 1;
      drop.y += drop.speed * step;

      if (drop.tick % drop.mutateEvery === 0) {
        // Mutate in place — never allocate.
        const idx = (Math.random() * drop.len) | 0;
        drop.glyphs[idx] = glyph();
        drop.glyphs[0] = glyph();
      }

      if (drop.y - drop.len > rows) {
        // Recycle drop object + glyph buffer.
        resetDrop(drop, rows);
      }
    }

    const x = (i * gap + (gap - fs) * 0.5) | 0;
    const headY = drop.y | 0;

    for (let n = 0; n < drop.len; n++) {
      const gy = headY - n;
      if (gy < -1 || gy > rows) continue;
      const ch = drop.glyphs[n];
      if (!ch) continue;
      if (n === 0) {
        ctx.fillStyle = headStyle;
      } else {
        const fade = 1 - n / drop.len;
        ctx.fillStyle = `rgba(${cr},${cg},${cb},${(fade * (0.3 + state.intensity * 0.55)).toFixed(3)})`;
      }
      ctx.fillText(ch, x, gy * fs);
    }
  }

  state.raf = requestAnimationFrame(() => paint(el));
}

function stopMatrix(el: MatrixEl): void {
  const state = el._ccsAbgMatrix;
  if (!state) return;
  if (state.raf != null) {
    cancelAnimationFrame(state.raf);
    state.raf = null;
  }
}

export function teardownMatrixBackground(el: HTMLElement): void {
  const node = el as MatrixEl;
  stopMatrix(node);
  node.classList.remove("is-matrix");
  const canvas = node.querySelector<HTMLCanvasElement>(".ccs-abg-matrix");
  if (canvas) canvas.hidden = true;
}

export function syncMatrixBackground(
  el: HTMLElement,
  _item: LayoutItem,
  _variant: MatrixBackgroundVariant,
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
  const node = el as MatrixEl;
  node.classList.add("is-matrix");
  const canvas = ensureCanvas(node);

  if (!node._ccsAbgMatrix) {
    node._ccsAbgMatrix = {
      raf: null,
      builtKey: "",
      speed: opts.speed,
      paused: opts.paused,
      intensity: opts.intensity,
      density: opts.density,
      colors: [opts.color, opts.color2, opts.color3],
      rain: parseRgb(opts.color, [0, 255, 102]),
      bg: parseRgb(opts.color2, [0, 20, 8]),
      head: parseRgb(opts.color3, [180, 255, 210]),
      drops: [],
      cols: 0,
      fontSize: 32,
      lastW: 0,
      lastH: 0,
      trail: 0.12,
      canvas,
      ctx: null
    };
  }

  const state = node._ccsAbgMatrix;
  state.speed = opts.speed;
  state.paused = opts.paused;
  state.intensity = opts.intensity;
  state.density = opts.density;
  state.colors = [opts.color, opts.color2, opts.color3];
  state.rain = parseRgb(opts.color, [0, 255, 102]);
  state.bg = parseRgb(opts.color2, [0, 20, 8]);
  state.head = parseRgb(opts.color3, [180, 255, 210]);
  state.trail = clamp(0.1 + (1 - state.intensity) * 0.12, 0.08, 0.22);
  state.canvas = canvas;

  const rect = node.getBoundingClientRect();
  const w = Math.max(2, Math.round(rect.width || node.clientWidth || 1920));
  const h = Math.max(2, Math.round(rect.height || node.clientHeight || 1080));
  const rebuildKey = `${opts.color},${opts.color2},${opts.color3}|${opts.density.toFixed(2)}|${w}x${h}`;

  if (!state.builtKey.startsWith(rebuildKey) || state.lastW === 0) {
    rebuild(node, w, h);
  }

  if (state.raf == null) {
    state.raf = requestAnimationFrame(() => paint(node));
  }

  if (!node.dataset.abgMatrixRo) {
    node.dataset.abgMatrixRo = "1";
    const ro = new ResizeObserver(() => {
      const st = node._ccsAbgMatrix;
      if (!st || !node.classList.contains("is-matrix")) return;
      const r = node.getBoundingClientRect();
      const nw = Math.max(2, Math.round(r.width || node.clientWidth || 1920));
      const nh = Math.max(2, Math.round(r.height || node.clientHeight || 1080));
      if (Math.abs(nw - st.lastW) > 8 || Math.abs(nh - st.lastH) > 8) {
        rebuild(node, nw, nh);
      }
    });
    ro.observe(node);
    node._ccsAbgMatrixRo = ro;
  }
}
