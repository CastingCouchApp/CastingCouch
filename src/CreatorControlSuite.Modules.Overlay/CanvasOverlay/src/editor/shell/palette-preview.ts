import type { LayoutItem } from "../../shared/types";
import type { PaletteEntry } from "./palette";

export interface ViewportSize {
  w: number;
  h: number;
}

export interface PalettePreviewController {
  attach(el: HTMLElement, entry: PaletteEntry): void;
  hide(): void;
  dispose(): void;
}

export interface PalettePreviewOptions {
  createItem: (type: string, kind: string) => LayoutItem;
  createContent: (item: LayoutItem) => HTMLElement;
  /** Optional: enrich item with demo props before render. */
  prepareItem?: (item: LayoutItem) => LayoutItem;
  /** Optional: paint live/demo data onto created content. */
  paintContent?: (el: HTMLElement, item: LayoutItem) => void;
  delayMs?: number;
  maxWidth?: number;
  maxHeight?: number;
  getViewport?: () => ViewportSize;
}

const PREVIEW_CLASS = "ccs-palette-preview";

export function computePreviewScale(
  width: number,
  height: number,
  maxWidth: number,
  maxHeight: number
): number {
  const w = Math.max(1, width);
  const h = Math.max(1, height);
  return Math.min(1, maxWidth / w, maxHeight / h);
}

export function positionPalettePreview(
  preview: HTMLElement,
  anchor: DOMRect,
  margin = 12,
  viewport: ViewportSize = { w: window.innerWidth, h: window.innerHeight }
): void {
  const pw = preview.offsetWidth || 0;
  const ph = preview.offsetHeight || 0;
  let left = anchor.right + margin;
  let top = anchor.top;
  if (left + pw > viewport.w - 8) {
    left = Math.max(8, anchor.left - margin - pw);
  }
  if (top + ph > viewport.h - 8) {
    top = Math.max(8, viewport.h - 8 - ph);
  }
  if (top < 8) top = 8;
  preview.style.left = `${Math.round(left)}px`;
  preview.style.top = `${Math.round(top)}px`;
}

export function mountPalettePreviewContent(
  stage: HTMLElement,
  item: LayoutItem,
  createContent: (item: LayoutItem) => HTMLElement,
  maxWidth = 280,
  maxHeight = 180,
  paintContent?: (el: HTMLElement, item: LayoutItem) => void
): { scale: number } {
  stage.innerHTML = "";
  const w = Math.max(1, item.w || 100);
  const h = Math.max(1, item.h || 100);
  const scale = computePreviewScale(w, h, maxWidth, maxHeight);

  const wrap = document.createElement("div");
  wrap.className = "ccs-item ccs-palette-preview-item";
  wrap.style.width = `${w}px`;
  wrap.style.height = `${h}px`;
  wrap.style.transform = `scale(${scale})`;
  wrap.style.transformOrigin = "top left";
  const content = createContent(item);
  content.dataset.role = "content";
  paintContent?.(content, item);
  wrap.appendChild(content);
  stage.appendChild(wrap);

  stage.style.width = `${Math.round(w * scale)}px`;
  stage.style.height = `${Math.round(h * scale)}px`;
  return { scale };
}

export function createPalettePreviewController(
  opts: PalettePreviewOptions
): PalettePreviewController {
  const delayMs = opts.delayMs ?? 220;
  const maxWidth = opts.maxWidth ?? 280;
  const maxHeight = opts.maxHeight ?? 180;
  const getViewport = opts.getViewport || (() => ({ w: window.innerWidth, h: window.innerHeight }));

  let timer: ReturnType<typeof setTimeout> | null = null;
  let popover: HTMLElement | null = null;
  let activeEl: HTMLElement | null = null;

  const clearTimer = () => {
    if (timer != null) {
      clearTimeout(timer);
      timer = null;
    }
  };

  const hide = () => {
    clearTimer();
    activeEl = null;
    if (popover) {
      popover.remove();
      popover = null;
    }
  };

  const show = (el: HTMLElement, entry: PaletteEntry) => {
    hide();
    activeEl = el;
    let item = opts.createItem(entry.type, entry.kind);
    if (opts.prepareItem) item = opts.prepareItem(item);
    popover = document.createElement("div");
    popover.className = PREVIEW_CLASS;
    popover.setAttribute("role", "tooltip");

    const title = document.createElement("div");
    title.className = "ccs-palette-preview-title";
    title.textContent = entry.label;

    const meta = document.createElement("div");
    meta.className = "ccs-palette-preview-meta";
    meta.textContent = entry.type;

    const stage = document.createElement("div");
    stage.className = "ccs-palette-preview-stage";
    try {
      mountPalettePreviewContent(
        stage,
        item,
        opts.createContent,
        maxWidth,
        maxHeight,
        opts.paintContent
      );
    } catch {
      const fallback = document.createElement("div");
      fallback.className = "ccs-muted";
      fallback.textContent = "Vorschau nicht verfügbar";
      stage.appendChild(fallback);
    }

    popover.appendChild(title);
    popover.appendChild(stage);
    popover.appendChild(meta);
    document.body.appendChild(popover);
    positionPalettePreview(popover, el.getBoundingClientRect(), 12, getViewport());
  };

  const attach = (el: HTMLElement, entry: PaletteEntry) => {
    el.addEventListener("pointerenter", () => {
      clearTimer();
      timer = setTimeout(() => show(el, entry), delayMs);
    });
    el.addEventListener("pointerleave", hide);
    el.addEventListener("dragstart", hide);
  };

  return {
    attach,
    hide,
    dispose() {
      hide();
    }
  };
}
