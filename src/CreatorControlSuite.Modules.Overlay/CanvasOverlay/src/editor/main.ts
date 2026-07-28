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
  const propEffects = document.getElementById("propEffects");
  const propAnimations = document.getElementById("propAnimations");
  syncProps(item, editorCtx, propExtra, propsEmpty, propsForm, btnDelete, propEffects, propAnimations);
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
  const propEffects = document.getElementById("propEffects");
  const propAnimations = document.getElementById("propAnimations");
  const btnDelete = document.getElementById("btnDelete") as HTMLButtonElement;

  const syncSelection = (item: LayoutItem | null) =>
    syncProps(item, editorCtx, propExtra, propsEmpty, propsForm, btnDelete, propEffects, propAnimations);

  editorRuntime = window.CcsCanvas.createRuntime({
    root: stage,
    editing: true,
    center: true,
    instanceId,
    onSelect: (item) => syncSelection(item),
    onChange: () => scheduleSave(),
    onAfterRender: ({ canvas }) => applyEditorLayers(canvas, prefs)
  });

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
  wireInspectorTabs();

  const widgets: PaletteEntry[] = [
    { type: "online", label: "Online + Zeit" },
    { type: "alert", label: "Alert" },
    { type: "music", label: "Music Player" },
    { type: "chat", label: "Chat" },
    { type: "ending-stats", label: "Ending Stats" },
    { type: "text", label: "Text" },
    { type: "image", label: "Image" },
    { type: "countdown", label: "Countdown" },
    { type: "socials", label: "Socials" },
    { type: "partner-roulette", label: "Partner Roulette" },
    { type: "goal-bar", label: "Goal Bar" },
    { type: "event-ticker", label: "Event Ticker" },
    { type: "viewer-count", label: "Viewer Count" },
    { type: "lower-third", label: "Lower Third" },
    { type: "qr-code", label: "QR Code" },
    { type: "brb-panel", label: "BRB Panel" },
    { type: "announcement-bar", label: "Announcement Bar" },
    { type: "animated-background", label: "Animated Background" }
  ];
  const shapes: PaletteEntry[] = [
    { type: "frame", label: "Frame" },
    { type: "frame.card", label: "Card Frame" },
    { type: "shape.vignette", label: "Vignette" },
    { type: "shape.cutout", label: "Cutout" },
    { type: "shape.scene-bg", label: "Starting Hintergrund" },
    { type: "shape.divider", label: "Divider" },
    { type: "shape.cam-ring", label: "Cam Ring" },
    { type: "shape.sticker", label: "Sticker" }
  ];

  fillPalette(document.getElementById("widgetPalette")!, widgets, "widget", addItem);
  fillPalette(document.getElementById("shapePalette")!, shapes, "shape", addItem);
  setupPaletteDrop(stage, addItem, () => editorRuntime.getLayout());

  const canvasSize = setupCanvasSize(editorRuntime, saveStatus, scheduleSave);
  setupDrag(
    stage,
    editorRuntime,
    (item) => syncSelection(item),
    scheduleSave,
    {
      isMagnetEnabled: () => prefs.magnet,
      isGridSnapEnabled: () => prefs.gridSnap,
      getGridDivisions: () => ({ gridH: prefs.gridH, gridV: prefs.gridV })
    }
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
    syncSelection(item);
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
  const gridSnapToggle = document.getElementById("toggleGridSnap") as HTMLInputElement;
  const magnetToggle = document.getElementById("toggleMagnet") as HTMLInputElement;
  const gridH = document.getElementById("gridHInput") as HTMLInputElement;
  const gridV = document.getElementById("gridVInput") as HTMLInputElement;

  obsToggle.checked = prefs.obsPreview;
  gridToggle.checked = prefs.grid;
  gridSnapToggle.checked = prefs.gridSnap;
  magnetToggle.checked = prefs.magnet;
  gridH.value = String(prefs.gridH);
  gridV.value = String(prefs.gridV);

  function persist(): void {
    prefs = {
      obsPreview: obsToggle.checked,
      grid: gridToggle.checked,
      gridH: Number(gridH.value) || 16,
      gridV: Number(gridV.value) || 6,
      gridSnap: gridSnapToggle.checked,
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
  gridSnapToggle.addEventListener("change", persist);
  magnetToggle.addEventListener("change", persist);
  gridH.addEventListener("change", persist);
  gridV.addEventListener("change", persist);
}

function isEditableKeyboardTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  const el = target as HTMLElement;
  const tag = (el.tagName || "").toLowerCase();
  if (tag === "input" || tag === "textarea" || tag === "select") return true;
  if (el.isContentEditable) return true;
  return !!el.closest("input, textarea, select, [contenteditable='true']");
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

  document.addEventListener("keydown", (e) => {
    if (e.key !== "Delete" && e.key !== "Backspace") return;
    if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return;
    if (isEditableKeyboardTarget(e.target)) return;
    if (!runEditorCommand("delete", editorRuntime, scheduleSave)) return;
    e.preventDefault();
    refreshSelectionUi();
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
  const layoutPane = document.getElementById("propsPaneLayout");
  if (!layoutPane) return;
  const ids = ["propX", "propY", "propW", "propH", "propZ"];
  const labels = layoutPane.querySelectorAll("label");
  const { root, body } = propSection("geometry", "Position & Größe", true);
  for (const label of Array.from(labels)) {
    const input = label.querySelector("input");
    if (input && ids.includes(input.id)) {
      body.appendChild(label);
    }
  }
  const locked = layoutPane.querySelector("#propLocked")?.closest("label");
  if (locked) body.appendChild(locked);
  layoutPane.appendChild(root);
}

const TAB_STORAGE_KEY = "ccs-props-tab";

function wireInspectorTabs(): void {
  const tabsRoot = document.getElementById("propsTabs");
  const form = document.getElementById("propsForm");
  if (!tabsRoot || !form) return;

  const tabs = Array.from(tabsRoot.querySelectorAll<HTMLButtonElement>(".ccs-props-tab"));
  const panes = Array.from(form.querySelectorAll<HTMLElement>(".ccs-props-pane"));

  function activate(tabId: string): void {
    const next = tabs.some((t) => t.dataset.tab === tabId) ? tabId : "layout";
    try {
      sessionStorage.setItem(TAB_STORAGE_KEY, next);
    } catch {
      /* ignore */
    }
    for (const tab of tabs) {
      const selected = tab.dataset.tab === next;
      tab.setAttribute("aria-selected", selected ? "true" : "false");
    }
    for (const pane of panes) {
      pane.hidden = pane.dataset.pane !== next;
    }
  }

  let stored = "layout";
  try {
    stored = sessionStorage.getItem(TAB_STORAGE_KEY) || "layout";
  } catch {
    /* ignore */
  }
  activate(stored);

  tabsRoot.addEventListener("click", (e) => {
    const btn = (e.target as HTMLElement).closest<HTMLButtonElement>(".ccs-props-tab");
    if (!btn || !btn.dataset.tab) return;
    activate(btn.dataset.tab);
  });
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
