import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./partner-roulette.css";

export const PARTNER_ROULETTE_TRANSITIONS = ["fade", "crossfade", "slide", "none"] as const;
export type PartnerRouletteTransition = (typeof PARTNER_ROULETTE_TRANSITIONS)[number];

/** Common CSS object-position keywords for the editor dropdown. */
export const PARTNER_ROULETTE_OBJECT_POSITIONS = [
  "center",
  "top",
  "bottom",
  "left",
  "right",
  "top left",
  "top right",
  "bottom left",
  "bottom right"
] as const;
export type PartnerRouletteObjectPosition = (typeof PARTNER_ROULETTE_OBJECT_POSITIONS)[number];

type RouletteEl = HTMLElement & {
  _timer?: ReturnType<typeof setTimeout> | null;
  _index?: number;
  _imagesKey?: string;
  _intervalMs?: number;
  _transition?: string;
  _transitionMs?: number;
  _transitioning?: boolean;
  _fit?: string;
  _radius?: number;
  _position?: string;
};

function normalizeTransition(raw: unknown): PartnerRouletteTransition {
  const value = String(raw || "fade").toLowerCase();
  return (PARTNER_ROULETTE_TRANSITIONS as readonly string[]).includes(value)
    ? (value as PartnerRouletteTransition)
    : "fade";
}

export function resolvePartnerRouletteImages(item: LayoutItem | null | undefined): string[] {
  const raw = prop(item, "images", []);
  if (!Array.isArray(raw)) {
    return [];
  }
  const out: string[] = [];
  for (const entry of raw) {
    if (typeof entry === "string") {
      const src = entry.trim();
      if (src) out.push(src);
      continue;
    }
    if (entry && typeof entry === "object") {
      const src = String((entry as { src?: unknown }).src || "").trim();
      if (src) out.push(src);
    }
  }
  return out;
}

function clearTimer(el: RouletteEl): void {
  if (el._timer != null) {
    clearTimeout(el._timer);
    el._timer = null;
  }
}

function applyAppearance(el: RouletteEl, item: LayoutItem): void {
  const fit = String(prop(item, "fit", "contain") || "contain");
  const safeFit = ["contain", "cover", "fill", "none", "scale-down"].includes(fit) ? fit : "contain";
  const radius = Math.max(0, Number(prop(item, "borderRadiusPx", 0)) || 0);
  const position = String(prop(item, "objectPosition", "center") || "center");
  const transition = normalizeTransition(prop(item, "transition", "fade"));
  const transitionMs = Math.max(0, Number(prop(item, "transitionMs", 500)) || 0);

  PARTNER_ROULETTE_TRANSITIONS.forEach((name) => {
    el.classList.remove("ccs-partner-roulette-t-" + name);
  });
  el.classList.add("ccs-partner-roulette-t-" + transition);
  el.style.setProperty("--ccs-roulette-fit", safeFit);
  el.style.setProperty("--ccs-roulette-radius", radius + "px");
  el.style.setProperty("--ccs-roulette-position", position);
  el.style.setProperty("--ccs-roulette-transition-ms", transitionMs + "ms");

  el._fit = safeFit;
  el._radius = radius;
  el._position = position;
  el._transition = transition;
  el._transitionMs = transitionMs;
}

function ensureSlides(el: RouletteEl): [HTMLImageElement, HTMLImageElement] {
  const stage = el.querySelector<HTMLElement>(".ccs-partner-roulette-stage");
  if (!stage) {
    throw new Error("partner-roulette stage missing");
  }
  let slides = Array.from(stage.querySelectorAll<HTMLImageElement>(".ccs-partner-roulette-slide"));
  while (slides.length < 2) {
    const img = document.createElement("img");
    img.className = "ccs-partner-roulette-slide";
    img.alt = "";
    img.draggable = false;
    stage.appendChild(img);
    slides = Array.from(stage.querySelectorAll<HTMLImageElement>(".ccs-partner-roulette-slide"));
  }
  return [slides[0], slides[1]];
}

function setSlideSrc(img: HTMLImageElement, src: string): void {
  if (img.getAttribute("src") !== src) {
    img.setAttribute("src", src);
  }
}

function showImmediate(el: RouletteEl, images: string[], index: number): void {
  const [a, b] = ensureSlides(el);
  const src = images[index] || "";
  setSlideSrc(a, src);
  a.classList.add("is-active");
  a.classList.remove("is-exit");
  b.classList.remove("is-active", "is-exit");
  b.removeAttribute("src");
  el._index = index;
  el._transitioning = false;
}

function advance(el: RouletteEl, images: string[]): void {
  if (!el.isConnected || el._transitioning) {
    return;
  }
  if (images.length < 2) {
    return;
  }

  const current = ((el._index || 0) % images.length + images.length) % images.length;
  const next = (current + 1) % images.length;
  const transition = el._transition || "fade";
  const transitionMs = el._transitionMs || 0;
  const [a, b] = ensureSlides(el);
  const active = a.classList.contains("is-active") ? a : b;
  const incoming = active === a ? b : a;

  setSlideSrc(incoming, images[next]);
  incoming.classList.remove("is-exit");
  // Commit index before the CSS transition finishes so data refreshes
  // (refreshAllData) cannot overwrite the incoming slide with the old src.
  el._index = next;

  if (transition === "none" || transitionMs <= 0) {
    active.classList.remove("is-active", "is-exit");
    incoming.classList.add("is-active");
    scheduleNext(el, images);
    return;
  }

  el._transitioning = true;

  if (transition === "fade") {
    active.classList.remove("is-active");
    window.setTimeout(() => {
      if (!el.isConnected) return;
      void incoming.offsetWidth;
      incoming.classList.add("is-active");
      active.classList.remove("is-exit");
      active.removeAttribute("src");
      el._transitioning = false;
      scheduleNext(el, images);
    }, transitionMs + 30);
    return;
  }

  // crossfade / slide: overlapping transition
  void incoming.offsetWidth;
  incoming.classList.add("is-active");
  if (transition === "slide") {
    active.classList.add("is-exit");
  }
  active.classList.remove("is-active");

  window.setTimeout(() => {
    if (!el.isConnected) return;
    active.classList.remove("is-exit");
    active.removeAttribute("src");
    el._transitioning = false;
    scheduleNext(el, images);
  }, transitionMs + 30);
}

function scheduleNext(el: RouletteEl, images: string[]): void {
  clearTimer(el);
  if (images.length < 2) {
    return;
  }
  // Do not require isConnected here: createItemContent runs before appendChild,
  // and the subsequent refresh often skips rescheduling when props are unchanged.
  // advance() already no-ops when the node is detached.
  const intervalMs = Math.max(500, Number(el._intervalMs) || 4000);
  el._timer = setTimeout(() => advance(el, images), intervalMs);
}

export function createPartnerRouletteEl(item?: LayoutItem): RouletteEl {
  const el = document.createElement("div") as RouletteEl;
  el.className = "ccs-partner-roulette ccs-partner-roulette-t-fade";
  el.innerHTML =
    `<div class="ccs-partner-roulette-stage"></div>` +
    `<div class="ccs-partner-roulette-placeholder">Partner-Bilder hinzufügen</div>`;
  el._index = 0;
  el._timer = null;
  el._transitioning = false;
  if (item) {
    updatePartnerRoulette(el, item);
  }
  return el;
}

export function updatePartnerRoulette(el: RouletteEl, item: LayoutItem): void {
  const images = resolvePartnerRouletteImages(item);
  const intervalMs = Math.max(500, Number(prop(item, "intervalMs", 4000)) || 4000);
  const imagesKey = images.join("\n");
  const transition = normalizeTransition(prop(item, "transition", "fade"));
  const transitionMs = Math.max(0, Number(prop(item, "transitionMs", 500)) || 0);

  const imagesChanged = el._imagesKey !== imagesKey;
  const timingChanged =
    el._intervalMs !== intervalMs ||
    el._transition !== transition ||
    el._transitionMs !== transitionMs;

  applyAppearance(el, item);
  el.classList.toggle("has-images", images.length > 0);
  el._imagesKey = imagesKey;
  el._intervalMs = intervalMs;
  el._transition = transition;
  el._transitionMs = transitionMs;

  if (images.length === 0) {
    clearTimer(el);
    const [a, b] = ensureSlides(el);
    a.classList.remove("is-active", "is-exit");
    b.classList.remove("is-active", "is-exit");
    a.removeAttribute("src");
    b.removeAttribute("src");
    el._index = 0;
    el._transitioning = false;
    return;
  }

  if (imagesChanged || el._index == null || el._index >= images.length) {
    clearTimer(el);
    el._transitioning = false;
    showImmediate(el, images, 0);
    scheduleNext(el, images);
    return;
  }

  // During a transition, do not touch slide src/classes — refreshAllData would
  // otherwise reset the incoming slide back to the previous image.
  if (el._transitioning) {
    return;
  }

  // Appearance-only update: keep current slide, restart timer if timing changed
  // or if the timer was never started (create-before-append race).
  const [a, b] = ensureSlides(el);
  const active = a.classList.contains("is-active") ? a : b.classList.contains("is-active") ? b : a;
  setSlideSrc(active, images[el._index]);
  if (timingChanged || el._timer == null) {
    scheduleNext(el, images);
  }
}
