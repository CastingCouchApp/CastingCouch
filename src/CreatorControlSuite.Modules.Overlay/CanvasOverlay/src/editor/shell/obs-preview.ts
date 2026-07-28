import type { CreateRuntime } from "../../shared/types";
import type { EditorPrefs } from "./editor-prefs";

function resolveCanvas(target: CreateRuntime | HTMLElement): HTMLElement {
  return "canvas" in target ? target.canvas : target;
}

/** Above typical item.z stacking; below magnet guides (9999). */
const GRID_Z_INDEX = "9000";

function ensureLayer(
  canvas: HTMLElement,
  className: string,
  placement: "back" | "front" = "back"
): HTMLElement {
  let el = canvas.querySelector(":scope > ." + className) as HTMLElement | null;
  if (!el) {
    el = document.createElement("div");
    el.className = className;
    el.setAttribute("aria-hidden", "true");
    if (placement === "front") {
      canvas.appendChild(el);
    } else {
      canvas.insertBefore(el, canvas.firstChild);
    }
  } else if (placement === "front" && canvas.lastElementChild !== el) {
    canvas.appendChild(el);
  }
  return el;
}

export function applyEditorLayers(target: CreateRuntime | HTMLElement, prefs: EditorPrefs): void {
  const canvas = resolveCanvas(target);
  const preview = ensureLayer(canvas, "ccs-obs-preview", "back");
  const grid = ensureLayer(canvas, "ccs-editor-grid", "front");

  preview.style.display = prefs.obsPreview ? "block" : "none";
  grid.style.zIndex = GRID_Z_INDEX;

  if (prefs.grid) {
    grid.style.display = "block";
    const h = Math.max(2, Math.min(64, prefs.gridH || 32));
    const v = Math.max(2, Math.min(64, prefs.gridV || 18));
    const xStep = 100 / h;
    const yStep = 100 / v;
    grid.style.backgroundImage = [
      `repeating-linear-gradient(90deg, rgba(255,255,255,.14) 0, rgba(255,255,255,.14) 1px, transparent 1px, transparent ${xStep}%)`,
      `repeating-linear-gradient(0deg, rgba(255,255,255,.14) 0, rgba(255,255,255,.14) 1px, transparent 1px, transparent ${yStep}%)`
    ].join(", ");
  } else {
    grid.style.display = "none";
  }
}

export function setObsPreviewImage(runtime: CreateRuntime, objectUrl: string | null): void {
  const preview = ensureLayer(runtime.canvas, "ccs-obs-preview");
  if (objectUrl) {
    preview.style.backgroundImage = `url("${objectUrl}")`;
  } else {
    preview.style.backgroundImage = "none";
  }
}

export function setupObsPreviewPolling(
  runtime: CreateRuntime,
  getPrefs: () => EditorPrefs,
  onStatus?: (text: string) => void
): { refreshNow: () => Promise<void>; stop: () => void } {
  let timer: number | null = null;
  let lastUrl: string | null = null;
  let running = false;

  async function refreshNow(): Promise<void> {
    const prefs = getPrefs();
    if (!prefs.obsPreview) {
      if (lastUrl) {
        URL.revokeObjectURL(lastUrl);
        lastUrl = null;
      }
      setObsPreviewImage(runtime, null);
      return;
    }
    if (document.visibilityState === "hidden") return;
    if (running) return;
    running = true;
    try {
      const res = await fetch("/obs/preview?t=" + Date.now(), { cache: "no-store" });
      if (!res.ok) {
        onStatus?.("OBS-Vorschau nicht verfügbar");
        return;
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      if (lastUrl) URL.revokeObjectURL(lastUrl);
      lastUrl = url;
      setObsPreviewImage(runtime, url);
    } catch {
      onStatus?.("OBS-Vorschau fehlgeschlagen");
    } finally {
      running = false;
    }
  }

  function tick(): void {
    void refreshNow();
  }

  timer = window.setInterval(tick, 1000);
  document.addEventListener("visibilitychange", tick);

  return {
    refreshNow,
    stop() {
      if (timer != null) window.clearInterval(timer);
      document.removeEventListener("visibilitychange", tick);
      if (lastUrl) URL.revokeObjectURL(lastUrl);
      lastUrl = null;
    }
  };
}
