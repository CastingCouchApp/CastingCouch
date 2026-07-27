import type { CreateRuntime } from "../../shared/types";

const FALLBACK_PRESETS = [
  { id: "1080p", label: "1920 × 1080 (Full HD)", width: 1920, height: 1080 },
  { id: "720p", label: "1280 × 720 (HD)", width: 1280, height: 720 },
  { id: "1440p", label: "2560 × 1440 (QHD)", width: 2560, height: 1440 },
  { id: "4k", label: "3840 × 2160 (4K)", width: 3840, height: 2160 },
  { id: "1080p-vert", label: "1080 × 1920 (Vertical)", width: 1080, height: 1920 },
  { id: "720p-vert", label: "720 × 1280 (Vertical)", width: 720, height: 1280 },
  { id: "square", label: "1080 × 1080 (Square)", width: 1080, height: 1080 }
];

export interface CanvasSizeApi {
  applyCanvasSize: (width: number | string, height: number | string) => boolean;
  syncCanvasSizeUi: () => void;
}

export function setupCanvasSize(
  runtime: CreateRuntime,
  saveStatus: HTMLElement,
  scheduleSave: () => void
): CanvasSizeApi {
  let sizePresets = FALLBACK_PRESETS.slice();
  const canvasSizePreset = document.getElementById("canvasSizePreset") as HTMLSelectElement;
  const canvasWidthInput = document.getElementById("canvasWidthInput") as HTMLInputElement;
  const canvasHeightInput = document.getElementById("canvasHeightInput") as HTMLInputElement;
  const canvasSizeBadge = document.getElementById("canvasSizeBadge")!;

  function fillSizePresets(list: typeof FALLBACK_PRESETS): void {
    sizePresets = list && list.length ? list : FALLBACK_PRESETS;
    canvasSizePreset.innerHTML = "";
    for (const p of sizePresets) {
      const opt = document.createElement("option");
      opt.value = p.id;
      opt.textContent = p.label;
      opt.dataset.w = String(p.width);
      opt.dataset.h = String(p.height);
      canvasSizePreset.appendChild(opt);
    }
    const custom = document.createElement("option");
    custom.value = "custom";
    custom.textContent = "Benutzerdefiniert…";
    canvasSizePreset.appendChild(custom);
  }

  function syncCanvasSizeUi(): void {
    const layout = runtime.getLayout();
    const w = Number(layout.canvasWidth) || 1920;
    const h = Number(layout.canvasHeight) || 1080;
    canvasWidthInput.value = String(w);
    canvasHeightInput.value = String(h);
    canvasSizeBadge.textContent = w + " × " + h;
    const match = sizePresets.find((p) => p.width === w && p.height === h);
    canvasSizePreset.value = match ? match.id : "custom";
  }

  function applyCanvasSize(width: number | string, height: number | string): boolean {
    const w = Math.round(Number(width));
    const h = Math.round(Number(height));
    if (!(w >= 320 && w <= 7680 && h >= 180 && h <= 4320)) {
      saveStatus.textContent = "Ungültige Größe (320×180 – 7680×4320)";
      return false;
    }
    const layout = runtime.getLayout();
    layout.canvasWidth = w;
    layout.canvasHeight = h;
    runtime.setLayout(layout, true);
    syncCanvasSizeUi();
    scheduleSave();
    return true;
  }

  canvasSizePreset.addEventListener("change", () => {
    if (canvasSizePreset.value === "custom") return;
    const opt = canvasSizePreset.selectedOptions[0];
    if (!opt) return;
    applyCanvasSize(opt.dataset.w!, opt.dataset.h!);
  });

  document.getElementById("btnApplyCanvasSize")!.addEventListener("click", () => {
    applyCanvasSize(canvasWidthInput.value, canvasHeightInput.value);
  });

  fillSizePresets(FALLBACK_PRESETS);
  void window.CcsCanvas.fetchJson("/canvas/size-presets").then((list) => {
    fillSizePresets(list as typeof FALLBACK_PRESETS);
    syncCanvasSizeUi();
  }).catch(() => syncCanvasSizeUi());

  return { applyCanvasSize, syncCanvasSizeUi };
}
