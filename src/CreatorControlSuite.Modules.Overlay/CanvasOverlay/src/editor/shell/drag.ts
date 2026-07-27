import type { CreateRuntime, LayoutItem } from "../../shared/types";
import { applyMagnetSnap, type MagnetGuide } from "./magnet";
import { getStageMetrics } from "./stage-metrics";

export interface DragOptions {
  isMagnetEnabled: () => boolean;
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

function paintGuides(canvas: HTMLElement, guides: MagnetGuide[], canvasW: number, canvasH: number): void {
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
    const id = (wrapper as HTMLElement).dataset.id!;
    const item = (runtime.getLayout().items || []).find((i) => i.id === id);
    if (!item) return;
    runtime.select(id);
    if (item.locked) return;
    const layout = runtime.getLayout();
    const metrics = getStageMetrics(stage, layout.canvasWidth || 1920, layout.canvasHeight || 1080);
    drag = {
      id,
      mode: handle ? (handle as HTMLElement).dataset.handle! : "move",
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
    if (options?.isMagnetEnabled()) {
      const snapped = applyMagnetSnap({ x, y, w, h }, otherRects(runtime, item.id), 8, mode);
      x = snapped.x;
      y = snapped.y;
      w = snapped.w;
      h = snapped.h;
      guides = snapped.guides;
    }

    item.x = x;
    item.y = y;
    item.w = w;
    item.h = h;

    const layout = runtime.getLayout();
    paintGuides(runtime.canvas, guides, layout.canvasWidth || 1920, layout.canvasHeight || 1080);

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
