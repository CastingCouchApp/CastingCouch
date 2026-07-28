import type { CreateRuntime, LayoutItem } from "../../shared/types";
import { cloneLayoutItem } from "./commands";
import { activeEdgesFromHandle, applyGridSnap } from "./grid-snap";
import { applyMagnetSnap, type MagnetGuide } from "./magnet";
import { computeSpacingHelpers, type SpacingHelper } from "./spacing";
import { getStageMetrics } from "./stage-metrics";

export interface DragOptions {
  isMagnetEnabled: () => boolean;
  isGridSnapEnabled: () => boolean;
  getGridDivisions: () => { gridH: number; gridV: number };
}

function otherRects(runtime: CreateRuntime, id: string): Array<{ x: number; y: number; w: number; h: number }> {
  return (runtime.getLayout().items || [])
    .filter((i) => i.id !== id)
    .map((i) => ({ x: i.x || 0, y: i.y || 0, w: i.w || 0, h: i.h || 0 }));
}

function ensureGuides(canvas: HTMLElement): HTMLElement {
  let el = canvas.querySelector(".ccs-magnet-guides") as HTMLElement | null;
  if (!el) {
    el = document.createElement("div");
    el.className = "ccs-magnet-guides";
    el.setAttribute("aria-hidden", "true");
    canvas.appendChild(el);
  }
  return el;
}

function paintGuides(
  canvas: HTMLElement,
  guides: MagnetGuide[],
  spacing: SpacingHelper[],
  canvasW: number,
  canvasH: number
): void {
  const host = ensureGuides(canvas);
  host.innerHTML = "";
  for (const g of guides) {
    const line = document.createElement("div");
    line.className = "ccs-magnet-guide " + (g.orientation === "v" ? "v" : "h");
    if (g.orientation === "v") {
      line.style.left = g.position + "px";
      line.style.top = "0";
      line.style.height = canvasH + "px";
    } else {
      line.style.top = g.position + "px";
      line.style.left = "0";
      line.style.width = canvasW + "px";
    }
    host.appendChild(line);
  }

  for (const s of spacing) {
    const line = document.createElement("div");
    line.className = "ccs-spacing-line " + (s.orientation === "v" ? "v" : "h");
    if (s.orientation === "v") {
      line.style.left = s.cross + "px";
      line.style.top = s.from + "px";
      line.style.height = Math.max(1, s.to - s.from) + "px";
    } else {
      line.style.top = s.cross + "px";
      line.style.left = s.from + "px";
      line.style.width = Math.max(1, s.to - s.from) + "px";
    }
    host.appendChild(line);

    const label = document.createElement("div");
    label.className = "ccs-spacing-label";
    label.textContent = s.distance + " px";
    label.style.left = s.labelX + "px";
    label.style.top = s.labelY + "px";
    host.appendChild(label);
  }
}

function clearGuides(canvas: HTMLElement): void {
  const host = canvas.querySelector(".ccs-magnet-guides");
  if (host) host.innerHTML = "";
}

export function setupDrag(
  stage: HTMLElement,
  runtime: CreateRuntime,
  syncProps: (item: LayoutItem | null) => void,
  scheduleSave: () => void,
  options?: DragOptions
): void {
  let drag: {
    id: string;
    mode: string;
    startX: number;
    startY: number;
    orig: { x: number; y: number; w: number; h: number };
    scale: number;
  } | null = null;

  stage.addEventListener("pointerdown", (e) => {
    if (e.button !== 0) return;
    const handle = (e.target as HTMLElement).closest(".ccs-handle");
    const wrapper = (e.target as HTMLElement).closest(".ccs-item");
    if (!wrapper) {
      runtime.select(null);
      return;
    }
    let id = (wrapper as HTMLElement).dataset.id!;
    let item = (runtime.getLayout().items || []).find((i) => i.id === id);
    if (!item) return;
    runtime.select(id);
    if (item.locked) return;
    const mode = handle ? (handle as HTMLElement).dataset.handle! : "move";

    // Alt+move: clone at same position and drag the clone.
    if (mode === "move" && e.altKey) {
      const layout = runtime.getLayout();
      const items = layout.items || [];
      const copy = cloneLayoutItem(item, items, 0, 0);
      layout.items = [...items, copy];
      runtime.setLayout(layout, true);
      runtime.renderItems();
      runtime.select(copy.id);
      item = copy;
      id = copy.id;
      syncProps(copy);
    }

    const layout = runtime.getLayout();
    const metrics = getStageMetrics(stage, layout.canvasWidth || 1920, layout.canvasHeight || 1080);
    drag = {
      id,
      mode,
      startX: e.clientX,
      startY: e.clientY,
      orig: { x: item.x, y: item.y, w: item.w, h: item.h },
      scale: metrics.scale
    };
    stage.setPointerCapture(e.pointerId);
    e.preventDefault();
  });

  stage.addEventListener("pointermove", (e) => {
    if (!drag) return;
    const item = (runtime.getLayout().items || []).find((i) => i.id === drag!.id);
    if (!item) return;
    const dx = (e.clientX - drag.startX) / drag.scale;
    const dy = (e.clientY - drag.startY) / drag.scale;
    let x = drag.orig.x;
    let y = drag.orig.y;
    let w = drag.orig.w;
    let h = drag.orig.h;
    let mode: "move" | "resize" = "move";
    const activeEdges = drag.mode === "move" ? undefined : activeEdgesFromHandle(drag.mode);

    if (drag.mode === "move") {
      x = drag.orig.x + dx;
      y = drag.orig.y + dy;
    } else {
      mode = "resize";
      if (drag.mode.includes("e")) w = Math.max(20, drag.orig.w + dx);
      if (drag.mode.includes("s")) h = Math.max(20, drag.orig.h + dy);
      if (drag.mode.includes("w")) {
        x = drag.orig.x + dx;
        w = Math.max(20, drag.orig.w - dx);
      }
      if (drag.mode.includes("n")) {
        y = drag.orig.y + dy;
        h = Math.max(20, drag.orig.h - dy);
      }
    }

    let guides: MagnetGuide[] = [];
    const layout = runtime.getLayout();
    const canvasW = layout.canvasWidth || 1920;
    const canvasH = layout.canvasHeight || 1080;
    const others = otherRects(runtime, item.id);

    // Grid first, then magnet (magnet can pull off-grid when near other widgets / canvas).
    if (options?.isGridSnapEnabled()) {
      const div = options.getGridDivisions();
      const snapped = applyGridSnap(
        { x, y, w, h },
        canvasW,
        canvasH,
        div.gridH,
        div.gridV,
        mode,
        undefined,
        undefined,
        mode === "resize" ? activeEdges : undefined
      );
      x = snapped.x;
      y = snapped.y;
      w = snapped.w;
      h = snapped.h;
      guides = snapped.guides;
    }
    if (options?.isMagnetEnabled()) {
      const snapped = applyMagnetSnap(
        { x, y, w, h },
        others,
        8,
        mode,
        mode === "resize" ? activeEdges : undefined,
        canvasW,
        canvasH
      );
      x = snapped.x;
      y = snapped.y;
      w = snapped.w;
      h = snapped.h;
      if (snapped.guides.length) {
        const axes = new Set(snapped.guides.map((g) => g.orientation));
        guides = guides.filter((g) => !axes.has(g.orientation)).concat(snapped.guides);
      }
    }

    item.x = x;
    item.y = y;
    item.w = w;
    item.h = h;

    const spacing =
      drag.mode === "move"
        ? computeSpacingHelpers({ x, y, w, h }, canvasW, canvasH, others)
        : [];
    paintGuides(runtime.canvas, guides, spacing, canvasW, canvasH);

    const node = runtime.itemNodes.get(item.id);
    if (node) {
      node.wrapper.style.left = item.x + "px";
      node.wrapper.style.top = item.y + "px";
      node.wrapper.style.width = item.w + "px";
      node.wrapper.style.height = item.h + "px";
    }
    syncProps(item);
  });

  stage.addEventListener("pointerup", () => {
    if (!drag) return;
    drag = null;
    clearGuides(runtime.canvas);
    scheduleSave();
  });
}
