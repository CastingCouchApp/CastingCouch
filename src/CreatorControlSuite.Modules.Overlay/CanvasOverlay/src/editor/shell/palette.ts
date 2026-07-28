import { clientToCanvas } from "./stage-metrics";
import type { PalettePreviewController } from "./palette-preview";

export interface PaletteEntry {
  type: string;
  label: string;
  category: string;
  kind: "widget" | "shape";
  keywords?: string;
}

export interface PaletteCategoryGroup {
  category: string;
  items: PaletteEntry[];
}

export function filterPaletteEntries(entries: PaletteEntry[], query: string): PaletteEntry[] {
  const q = query.trim().toLowerCase();
  if (!q) return entries.slice();
  return entries.filter((entry) => {
    const hay = [
      entry.label,
      entry.type,
      entry.category,
      entry.kind,
      entry.keywords || ""
    ]
      .join(" ")
      .toLowerCase();
    return hay.includes(q);
  });
}

export function groupPaletteByCategory(entries: PaletteEntry[]): PaletteCategoryGroup[] {
  const order: string[] = [];
  const map = new Map<string, PaletteEntry[]>();
  for (const entry of entries) {
    if (!map.has(entry.category)) {
      map.set(entry.category, []);
      order.push(entry.category);
    }
    map.get(entry.category)!.push(entry);
  }
  return order.map((category) => ({
    category,
    items: map.get(category)!
  }));
}

function appendPaletteItem(
  host: HTMLElement,
  entry: PaletteEntry,
  addItem: (type: string, kind: string, x: number, y: number) => void,
  preview?: PalettePreviewController | null
): void {
  const btn = document.createElement("div");
  btn.className = "ccs-palette-item";
  btn.textContent = entry.label;
  btn.draggable = true;
  btn.dataset.type = entry.type;
  btn.dataset.kind = entry.kind;
  btn.addEventListener("dragstart", (e) => {
    e.dataTransfer!.setData(
      "application/ccs-item",
      JSON.stringify({ type: entry.type, kind: entry.kind })
    );
  });
  btn.addEventListener("dblclick", () => {
    addItem(entry.type, entry.kind, 120, 120);
  });
  preview?.attach(btn, entry);
  host.appendChild(btn);
}

/** Flat list (legacy helper). */
export function fillPalette(
  el: HTMLElement,
  items: PaletteEntry[],
  kind: string,
  addItem: (type: string, kind: string, x: number, y: number) => void,
  preview?: PalettePreviewController | null
): void {
  el.innerHTML = "";
  for (const entry of items) {
    appendPaletteItem(
      el,
      { ...entry, kind: (entry.kind || kind) as "widget" | "shape" },
      addItem,
      preview
    );
  }
}

/**
 * Renders category `<details>` groups. When `query` is non-empty, matching
 * categories stay open and empty categories are omitted.
 */
export function fillCategorizedPalette(
  el: HTMLElement,
  items: PaletteEntry[],
  addItem: (type: string, kind: string, x: number, y: number) => void,
  query = "",
  preview?: PalettePreviewController | null
): void {
  el.innerHTML = "";
  const filtered = filterPaletteEntries(items, query);

  if (!filtered.length) {
    const empty = document.createElement("p");
    empty.className = "ccs-muted ccs-palette-empty";
    empty.textContent = "Keine Treffer";
    el.appendChild(empty);
    return;
  }

  for (const group of groupPaletteByCategory(filtered)) {
    const details = document.createElement("details");
    details.className = "ccs-palette-category";
    details.open = true;
    details.dataset.category = group.category;

    const summary = document.createElement("summary");
    summary.className = "ccs-palette-category-summary";
    summary.textContent = group.category;

    const body = document.createElement("div");
    body.className = "ccs-palette-category-body ccs-palette-list";
    for (const entry of group.items) {
      appendPaletteItem(body, entry, addItem, preview);
    }

    details.appendChild(summary);
    details.appendChild(body);
    el.appendChild(details);
  }
}

export function setupPaletteSearch(
  input: HTMLInputElement,
  render: (query: string) => void
): void {
  const run = () => render(input.value);
  input.addEventListener("input", run);
  input.addEventListener("search", run);
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
