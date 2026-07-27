import { clientToCanvas } from "./stage-metrics";

export interface PaletteEntry {
  type: string;
  label: string;
}

export function fillPalette(
  el: HTMLElement,
  items: PaletteEntry[],
  kind: string,
  addItem: (type: string, kind: string, x: number, y: number) => void
): void {
  el.innerHTML = "";
  for (const entry of items) {
    const btn = document.createElement("div");
    btn.className = "ccs-palette-item";
    btn.textContent = entry.label;
    btn.draggable = true;
    btn.addEventListener("dragstart", (e) => {
      e.dataTransfer!.setData("application/ccs-item", JSON.stringify({ type: entry.type, kind }));
    });
    btn.addEventListener("dblclick", () => {
      addItem(entry.type, kind, 120, 120);
    });
    el.appendChild(btn);
  }
}

/** Stage drop once — must not be registered per palette fill. */
export function setupPaletteDrop(
  stage: HTMLElement,
  addItem: (type: string, kind: string, x: number, y: number) => void,
  getLayout: () => { canvasWidth: number; canvasHeight: number }
): void {
  stage.addEventListener("dragover", (e) => e.preventDefault());
  stage.addEventListener("drop", (e) => {
    e.preventDefault();
    try {
      const payload = JSON.parse(e.dataTransfer!.getData("application/ccs-item") || "{}") as {
        type?: string;
        kind?: string;
      };
      if (!payload.type || !payload.kind) return;
      const layout = getLayout();
      const canvasWidth = layout.canvasWidth || 1920;
      const canvasHeight = layout.canvasHeight || 1080;
      const { x, y } = clientToCanvas(stage, e.clientX, e.clientY, canvasWidth, canvasHeight);
      addItem(payload.type, payload.kind, Math.max(0, Math.round(x)), Math.max(0, Math.round(y)));
    } catch {
      /* ignore */
    }
  });
}
