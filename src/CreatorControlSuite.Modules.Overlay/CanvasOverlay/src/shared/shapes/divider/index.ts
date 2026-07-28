import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import "./divider.css";

export const DIVIDER_VARIANTS = [
  "line",
  "dashed",
  "dotted",
  "double",
  "gradient",
  "glow",
  "flourish",
  "bracket",
  "diamond",
  "chevron",
  "wave",
  "pixel"
] as const;

export type DividerVariant = (typeof DIVIDER_VARIANTS)[number];

/** Alias for editor palettes that expect DIVIDER_STYLES */
export const DIVIDER_STYLES = DIVIDER_VARIANTS;

export const DIVIDER_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }> = {
  thin: { w: 400, h: 8, label: "Thin" },
  standard: { w: 600, h: 16, label: "Standard" },
  ornate: { w: 800, h: 32, label: "Ornate" }
};

function dividerVariant(item: LayoutItem | null | undefined): DividerVariant {
  const raw = String(prop(item, "variant", "line") || prop(item, "style", "line") || "line").toLowerCase();
  return (DIVIDER_VARIANTS as readonly string[]).includes(raw) ? (raw as DividerVariant) : "line";
}

function ensureMotif(el: HTMLElement, motif: string): void {
  let node = el.querySelector<HTMLElement>(".ccs-divider-motif");
  if (motif === "none") {
    node?.remove();
    return;
  }
  if (!node) {
    node = document.createElement("span");
    node.className = "ccs-divider-motif";
    el.appendChild(node);
  }
  node.dataset.motif = motif;
  node.textContent = motif === "diamond" ? "◆" : motif === "star" ? "★" : motif === "dot" ? "●" : "✦";
}

export function applyDivider(el: HTMLElement, item: LayoutItem): void {
  const variant = dividerVariant(item);
  DIVIDER_VARIANTS.forEach((name) => el.classList.remove("ccs-divider-v-" + name));
  el.classList.add("ccs-divider-v-" + variant);
  el.dataset.variant = variant;

  const orientation = String(prop(item, "orientation", "h") || "h").toLowerCase() === "v" ? "v" : "h";
  el.classList.toggle("ccs-divider-orient-v", orientation === "v");
  el.classList.toggle("ccs-divider-orient-h", orientation === "h");

  const thickness = Math.max(1, Number(prop(item, "thickness", 2)) || 2);
  const opacity = Math.max(0, Math.min(1, Number(prop(item, "opacity", 1)) || 1));
  const lengthMode = String(prop(item, "lengthMode", "fill") || "fill").toLowerCase() === "percent" ? "percent" : "fill";
  const lengthPercent = Math.max(10, Math.min(100, Number(prop(item, "lengthPercent", 80)) || 80));
  const align = String(prop(item, "align", "center") || "center");
  const color = String(prop(item, "color", "#ff7a00") || "#ff7a00");
  const color2 = String(prop(item, "color2", "#ffffff") || "#ffffff");
  const showCenterMotif = prop(item, "showCenterMotif", false) === true;
  const motif = showCenterMotif
    ? String(prop(item, "motif", "diamond") || "diamond").toLowerCase()
    : "none";
  const motifSize = Math.max(8, Number(prop(item, "motifSize", 18)) || 18);
  const animateShimmer = prop(item, "animateShimmer", false) === true;

  el.style.setProperty("--ccs-divider-thickness", thickness + "px");
  el.style.setProperty("--ccs-divider-opacity", String(opacity));
  el.style.setProperty("--ccs-divider-color", color);
  el.style.setProperty("--ccs-divider-color2", color2);
  el.style.setProperty("--ccs-divider-align", align);
  el.style.setProperty("--ccs-divider-motif-size", motifSize + "px");
  el.style.setProperty("--ccs-divider-length", lengthMode === "percent" ? lengthPercent + "%" : "100%");
  el.classList.toggle("ccs-divider-shimmer", animateShimmer);

  ensureMotif(el, motif);
}

export function createDividerEl(item: LayoutItem): HTMLElement {
  const el = document.createElement("div");
  el.className = "ccs-shape ccs-divider ccs-divider-v-line ccs-divider-orient-h";
  el.innerHTML = `<span class="ccs-divider-line" aria-hidden="true"></span>`;
  applyDivider(el, item);
  return el;
}

export function updateDivider(el: HTMLElement, item: LayoutItem): void {
  applyDivider(el, item);
}
