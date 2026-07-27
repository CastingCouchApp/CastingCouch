import "../editor/editor-shell.css";
import type { CreateRuntime, LayoutItem } from "../../shared/types";
import type { EditorContext } from "./props/context";
import { syncProps } from "./props/sync-props";
import { propSection, featureSection } from "./sections/prop-section";
import { fillPalette, setupPaletteDrop, type PaletteEntry } from "./shell/palette";
import { setupCanvasSize } from "./shell/canvas-size";
import { setupDrag } from "./shell/drag";
import { createSaveScheduler } from "./shell/save";
import { loadEditorPrefs, saveEditorPrefs, type EditorPrefs } from "./shell/editor-prefs";
import { applyEditorLayers, setupObsPreviewPolling } from "./shell/obs-preview";
import { runEditorCommand } from "./shell/commands";
import { setupContextMenu } from "./shell/context-menu";

let editorRuntime: CreateRuntime;
let editorCtx: EditorContext;
let scheduleSave: () => void;
let prefs: EditorPrefs = loadEditorPrefs();

function liveItem(from?: LayoutItem | null): LayoutItem | null {
  const id = (from && from.id) || editorRuntime.getSelectedId();
  if (!id) return null;
  return (editorRuntime.getLayout().items || []).find((i) => i.id === id) || null;
}

function commitProp(from: LayoutItem, apply: (live: LayoutItem) => void): LayoutItem | null {
  const item = liveItem(from);
  if (!item || item.locked) return null;
  item.props = item.props || {};
  apply(item);
  editorRuntime.renderItems();
  applyEditorLayers(editorRuntime, prefs);
  editorRuntime.select(item.id);
  scheduleSave();
  return item;
}

function previewProp(from: LayoutItem, apply: (live: LayoutItem) => void): LayoutItem | null {
  const item = liveItem(from);
  if (!item || item.locked) return null;
  item.props = item.props || {};
  apply(item);
  editorRuntime.renderItems();
  applyEditorLayers(editorRuntime, prefs);
  return item;
}

function selectedItem(): LayoutItem | null {
  const id = editorRuntime.getSelectedId();
  return (editorRuntime.getLayout().items || []).find((i) => i.id === id) || null;
}

function refreshSelectionUi(): void {
  const item = selectedItem();
  const btnDelete = document.getElementById("btnDelete") as HTMLButtonElement;
  const propsEmpty = document.getElementById("propsEmpty")!;
  const propsForm = document.getElementById("propsForm")!;
  const propExtra = document.getElementById("propExtra")!;
  syncProps(item, editorCtx, propExtra, propsEmpty, propsForm, btnDelete);
}

function bootEditor(): void {
  const params = new URLSearchParams(location.search);
  const instanceId = (location.pathname.split("/").filter(Boolean).pop() || params.get("id") || "").trim();

  function setInstanceLabel(name?: string): void {
    const label = document.getElementById("instanceLabel");
    if (!label) return;
    const title = (name || "").trim();
    label.textContent = title
      ? ("Canvas: " + title + " (" + (instanceId || "–") + ")")
      : ("Canvas: " + (instanceId || "–"));
  }
  setInstanceLabel("");

  const stage = document.getElementById("stage")!;
  const saveStatus = document.getElementById("saveStatus")!;
  const propsEmpty = document.getElementById("propsEmpty")!;
  const propsForm = document.getElementById("propsForm")!;
  const propExtra = document.getElementById("propExtra")!;
  const btnDelete = document.getElementById("btnDelete") as HTMLButtonElement;

  editorRuntime = window.CcsCanvas.createRuntime({
    root: stage,
    editing: true,
    center: true,
    instanceId,
    onSelect: (item) => syncProps(item, editorCtx, propExtra, propsEmpty, propsForm, btnDelete),
    onChange: () => scheduleSave()
  });

  const originalRender = editorRuntime.renderItems.bind(editorRuntime);
  editorRuntime.renderItems = () => {
    originalRender();
    applyEditorLayers(editorRuntime, prefs);
  };

  const save = createSaveScheduler(editorRuntime, instanceId, saveStatus, () => ws);
  scheduleSave = save.scheduleSave;
  let ws: WebSocket | null = null;

  editorCtx = {
    runtime: editorRuntime,
    scheduleSave,
    liveItem,
    commitProp,
    previewProp
  };

  wrapGeometrySection();

  const widgets: PaletteEntry[] = [
    { type: "online", label: "Online + Zeit" },
    { type: "alert", label: "Alert" },
    { type: "music", label: "Music Player" },
    { type: "chat", label: "Chat" },
    { type: "ending-stats", label: "Ending Stats" },
    { type: "text", label: "Text" },
    { type: "image", label: "Image" },
    { type: "countdown", label: "Countdown" },
    { type: "socials", label: "Socials" }
  ];
  const shapes: PaletteEntry[] = [
    { type: "frame", label: "Frame" },
    { type: "frame.card", label: "Card Frame" },
    { type: "shape.vignette", label: "Vignette" },
    { type: "shape.cutout", label: "Cutout" },
    { type: "shape.scene-bg", label: "Starting Hintergrund" }
  ];

  fillPalette(document.getElementById("widgetPalette")!, widgets, "widget", addItem);
  fillPalette(document.getElementById("shapePalette")!, shapes, "shape", addItem);
  setupPaletteDrop(stage, addItem, () => editorRuntime.getLayout());

  const canvasSize = setupCanvasSize(editorRuntime, saveStatus, scheduleSave);
  setupDrag(
    stage,
    editorRuntime,
    (item) => syncProps(item, editorCtx, propExtra, propsEmpty, propsForm, btnDelete),
    scheduleSave,
    { isMagnetEnabled: () => prefs.magnet }
  );
  setupContextMenu(stage, editorRuntime, scheduleSave, refreshSelectionUi);

  wireCommands();
  wireObsSize(canvasSize, saveStatus);

  const obsPolling = setupObsPreviewPolling(editorRuntime, () => prefs, (text) => {
    saveStatus.textContent = text;
  });
  wireToolbarPrefs(saveStatus, () => void obsPolling.refreshNow());
  applyEditorLayers(editorRuntime, prefs);
  if (prefs.obsPreview) void obsPolling.refreshNow();

  ["propX", "propY", "propW", "propH", "propZ"].forEach((id) => {
    document.getElementById(id)!.addEventListener("change", () => {
      const item = selectedItem();
      if (!item || item.locked) return;
      item.x = Number((document.getElementById("propX") as HTMLInputElement).value) || 0;
      item.y = Number((document.getElementById("propY") as HTMLInputElement).value) || 0;
      item.w = Math.max(20, Number((document.getElementById("propW") as HTMLInputElement).value) || 20);
      item.h = Math.max(20, Number((document.getElementById("propH") as HTMLInputElement).value) || 20);
      item.z = Number((document.getElementById("propZ") as HTMLInputElement).value) || 0;
      editorRuntime.renderItems();
      editorRuntime.select(item.id);
      scheduleSave();
    });
  });

  document.getElementById("propLocked")!.addEventListener("change", (e) => {
    const item = selectedItem();
    if (!item) return;
    item.locked = (e.target as HTMLInputElement).checked;
    scheduleSave();
    syncProps(item, editorCtx, propExtra, propsEmpty, propsForm, btnDelete);
  });

  void boot(instanceId, setInstanceLabel, () => {
    ws = window.CcsCanvas.connectWs((evt) => {
      editorRuntime.handleRealtime(evt);
    });
  });
}

function wireToolbarPrefs(saveStatus: HTMLElement, onObsPreviewChanged: () => void): void {
  const obsToggle = document.getElementById("toggleObsPreview") as HTMLInputElement;
  const gridToggle = document.getElementById("toggleGrid") as HTMLInputElement;
  const magnetToggle = document.getElementById("toggleMagnet") as HTMLInputElement;
  const gridH = document.getElementById("gridHInput") as HTMLInputElement;
  const gridV = document.getElementById("gridVInput") as HTMLInputElement;

  obsToggle.checked = prefs.obsPreview;
  gridToggle.checked = prefs.grid;
  magnetToggle.checked = prefs.magnet;
  gridH.value = String(prefs.gridH);
  gridV.value = String(prefs.gridV);

  function persist(): void {
    prefs = {
      obsPreview: obsToggle.checked,
      grid: gridToggle.checked,
      gridH: Number(gridH.value) || 16,
      gridV: Number(gridV.value) || 6,
      magnet: magnetToggle.checked
    };
    saveEditorPrefs(prefs);
    applyEditorLayers(editorRuntime, prefs);
  }

  obsToggle.addEventListener("change", () => {
    persist();
    onObsPreviewChanged();
    if (prefs.obsPreview) {
      saveStatus.textContent = "OBS-Vorschau aktiv";
    }
  });
  gridToggle.addEventListener("change", persist);
  magnetToggle.addEventListener("change", persist);
  gridH.addEventListener("change", persist);
  gridV.addEventListener("change", persist);
}

function wireCommands(): void {
  document.getElementById("btnDelete")!.addEventListener("click", () => {
    runEditorCommand("delete", editorRuntime, scheduleSave);
    refreshSelectionUi();
  });
  document.getElementById("btnFront")!.addEventListener("click", () => {
    runEditorCommand("bringFront", editorRuntime, scheduleSave);
  });
  document.getElementById("btnBack")!.addEventListener("click", () => {
    runEditorCommand("sendBack", editorRuntime, scheduleSave);
  });
  document.getElementById("btnDuplicate")?.addEventListener("click", () => {
    runEditorCommand("duplicate", editorRuntime, scheduleSave);
    refreshSelectionUi();
  });
  document.getElementById("btnLayerUp")?.addEventListener("click", () => {
    runEditorCommand("layerUp", editorRuntime, scheduleSave);
  });
  document.getElementById("btnLayerDown")?.addEventListener("click", () => {
    runEditorCommand("layerDown", editorRuntime, scheduleSave);
  });
}

function wireObsSize(
  canvasSize: { applyCanvasSize: (w: number | string, h: number | string) => boolean },
  saveStatus: HTMLElement
): void {
  const obsSizeLabel = document.getElementById("obsSizeLabel");
  const btn = document.getElementById("btnApplyObsSize");

  async function refreshObsSize(): Promise<void> {
    try {
      const data = await window.CcsCanvas.fetchJson("/obs/video-settings") as {
        connected?: boolean;
        baseWidth?: number;
        baseHeight?: number;
      };
      if (obsSizeLabel) {
        if (data && data.connected && data.baseWidth && data.baseHeight) {
          obsSizeLabel.textContent = "OBS: " + data.baseWidth + " × " + data.baseHeight;
        } else {
          obsSizeLabel.textContent = "OBS: nicht verbunden";
        }
      }
    } catch {
      if (obsSizeLabel) obsSizeLabel.textContent = "OBS: nicht verbunden";
    }
  }

  btn?.addEventListener("click", async () => {
    try {
      const data = await window.CcsCanvas.fetchJson("/obs/video-settings") as {
        connected?: boolean;
        baseWidth?: number;
        baseHeight?: number;
      };
      if (!data?.connected || !data.baseWidth || !data.baseHeight) {
        saveStatus.textContent = "OBS-Größe nicht verfügbar";
        return;
      }
      if (canvasSize.applyCanvasSize(data.baseWidth, data.baseHeight)) {
        saveStatus.textContent = "Canvas-Größe von OBS übernommen";
      }
    } catch {
      saveStatus.textContent = "OBS-Größe konnte nicht gelesen werden";
    }
  });

  void refreshObsSize();
  window.setInterval(() => void refreshObsSize(), 5000);
}

function wrapGeometrySection(): void {
  const form = document.getElementById("propsForm");
  if (!form) return;
  const ids = ["propX", "propY", "propW", "propH", "propZ"];
  const labels = form.querySelectorAll("label");
  const { root, body } = propSection("geometry", "Position & Größe", true);
  for (const label of Array.from(labels)) {
    const input = label.querySelector("input");
    if (input && ids.includes(input.id)) {
      body.appendChild(label);
    }
  }
  const locked = form.querySelector("#propLocked")?.closest("label");
  if (locked) body.appendChild(locked);
  form.insertBefore(root, document.getElementById("propExtra"));
}

function addItem(type: string, kind: string, x: number, y: number): void {
  const layout = editorRuntime.getLayout();
  const item = editorRuntime.createItem(type, kind, x, y);
  layout.items = layout.items || [];
  layout.items.push(item);
  editorRuntime.setLayout(layout, true);
  editorRuntime.select(item.id);
  scheduleSave();
}

async function boot(
  instanceId: string,
  setInstanceLabel: (name?: string) => void,
  connect: () => void
): Promise<void> {
  if (!instanceId) {
    (document.getElementById("saveStatus")!).textContent = "URL: /editor/{instanceId}";
    return;
  }
  try {
    const layout = (await window.CcsCanvas.fetchJson("/layout/" + encodeURIComponent(instanceId))) as LayoutItem & { items?: LayoutItem[]; name?: string };
    editorRuntime.setLayout(layout as never);
    setInstanceLabel(layout && (layout as { name?: string }).name);
  } catch {
    editorRuntime.setLayout({ ...window.CcsCanvas.DEFAULT_LAYOUT, items: [] });
    setInstanceLabel("");
  }
  try {
    editorRuntime.setData((await window.CcsCanvas.fetchJson("/data/overlay-data.json")) as Record<string, unknown>);
  } catch { /* optional */ }
  try {
    editorRuntime.setChatConfig(await window.CcsCanvas.fetchJson("/chat/config"));
  } catch { /* optional */ }
  await editorRuntime.loadChatHistory();
  await window.CcsCanvas.loadExtensions();
  applyEditorLayers(editorRuntime, prefs);
  connect();
  setInterval(async () => {
    try {
      editorRuntime.setData((await window.CcsCanvas.fetchJson("/data/overlay-data.json")) as Record<string, unknown>);
    } catch { /* ignore */ }
  }, 2000);
}

bootEditor();

export { liveItem, commitProp, featureSection };
