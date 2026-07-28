import "../editor/editor-shell.css";
import type { CreateRuntime, LayoutItem } from "../../shared/types";
import type { EditorContext } from "./props/context";
import { syncProps } from "./props/sync-props";
import { propSection, featureSection } from "./sections/prop-section";
import { fillCategorizedPalette, setupPaletteDrop, setupPaletteSearch, type PaletteEntry } from "./shell/palette";
import { createPalettePreviewController } from "./shell/palette-preview";
import {
  applyPaletteDemoProps,
  demoAlertPayload,
  demoChatMessages,
  demoTickerEvents,
  PALETTE_DEMO_DATA
} from "./shell/palette-demo";
import { setupCanvasSize } from "./shell/canvas-size";
import { setupDrag } from "./shell/drag";
import { createSaveScheduler } from "./shell/save";
import { loadEditorPrefs, saveEditorPrefs, type EditorPrefs } from "./shell/editor-prefs";
import { applyEditorLayers, setupObsPreviewPolling } from "./shell/obs-preview";
import { runEditorCommand } from "./shell/commands";
import { setupContextMenu } from "./shell/context-menu";
import { wireInspectorTabs } from "./shell/inspector-tabs";

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
    const text = title || instanceId || "–";
    label.textContent = text;
    label.title = title && instanceId && title !== instanceId
      ? `${title} (${instanceId})`
      : text;
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

  const paletteItems: PaletteEntry[] = [
    // Live
    { type: "online", label: "Online + Zeit", category: "Live", kind: "widget", keywords: "uptime clock status" },
    { type: "alert", label: "Alert", category: "Live", kind: "widget", keywords: "benachrichtigung" },
    { type: "viewer-count", label: "Viewer Count", category: "Live", kind: "widget", keywords: "zuschauer" },
    { type: "event-ticker", label: "Event Ticker", category: "Live", kind: "widget", keywords: "follows subs bits" },
    { type: "goal-bar", label: "Goal Bar", category: "Live", kind: "widget", keywords: "ziel follower" },
    { type: "ending-stats", label: "Ending Stats", category: "Live", kind: "widget", keywords: "statistik ende" },
    // Interaktion
    { type: "chat", label: "Chat", category: "Interaktion", kind: "widget" },
    { type: "socials", label: "Socials", category: "Interaktion", kind: "widget", keywords: "twitch youtube discord" },
    { type: "partner-roulette", label: "Partner Roulette", category: "Interaktion", kind: "widget", keywords: "logos sponsor" },
    { type: "announcement-bar", label: "Announcement Bar", category: "Interaktion", kind: "widget", keywords: "marquee ticker" },
    // Content
    { type: "text", label: "Text", category: "Content", kind: "widget" },
    { type: "image", label: "Image", category: "Content", kind: "widget", keywords: "bild" },
    { type: "countdown", label: "Countdown", category: "Content", kind: "widget", keywords: "timer" },
    { type: "lower-third", label: "Lower Third", category: "Content", kind: "widget", keywords: "name titel" },
    { type: "brb-panel", label: "BRB Panel", category: "Content", kind: "widget", keywords: "starting pause" },
    { type: "qr-code", label: "QR Code", category: "Content", kind: "widget" },
    { type: "music", label: "Music Player", category: "Content", kind: "widget", keywords: "spotify youtube" },
    // Hintergrund
    { type: "animated-background", label: "Animated Background", category: "Hintergrund", kind: "widget", keywords: "bg parallax" },
    { type: "shape.scene-bg", label: "Starting Hintergrund", category: "Hintergrund", kind: "shape", keywords: "scene bg" },
    // Frames & Shapes
    { type: "frame", label: "Frame", category: "Frames", kind: "shape", keywords: "rahmen" },
    { type: "frame.card", label: "Card Frame", category: "Frames", kind: "shape" },
    { type: "shape.cam-ring", label: "Cam Ring", category: "Frames", kind: "shape", keywords: "webcam" },
    { type: "shape.vignette", label: "Vignette", category: "Masken", kind: "shape" },
    { type: "shape.cutout", label: "Cutout", category: "Masken", kind: "shape", keywords: "loch maske" },
    { type: "shape.divider", label: "Divider", category: "Deko", kind: "shape", keywords: "linie" },
    { type: "shape.sticker", label: "Sticker", category: "Deko", kind: "shape" }
  ];

  const paletteRoot = document.getElementById("paletteRoot")!;
  const palettePreview = createPalettePreviewController({
    createItem: (type, kind) => editorRuntime.createItem(type, kind, 0, 0),
    prepareItem: applyPaletteDemoProps,
    createContent: (item) => window.CcsCanvas.createItemContent(item),
    paintContent: (el, item) => {
      window.CcsCanvas.paintItemContent(el, item, PALETTE_DEMO_DATA, null, {
        seedDemo: true,
        demoChatMessages: demoChatMessages(),
        demoAlert: demoAlertPayload(),
        demoTickerEvents: demoTickerEvents()
      });
    }
  });
  const renderPalette = (query: string) => {
    palettePreview.hide();
    fillCategorizedPalette(paletteRoot, paletteItems, addItem, query, palettePreview);
  };
  renderPalette("");
  const searchInput = document.getElementById("paletteSearch") as HTMLInputElement | null;
  if (searchInput) setupPaletteSearch(searchInput, renderPalette);
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

  ["propX", "propY", "propW", "propH", "propZ", "propPadding"].forEach((id) => {
    document.getElementById(id)!.addEventListener("change", () => {
      const item = selectedItem();
      if (!item || item.locked) return;
      item.x = Number((document.getElementById("propX") as HTMLInputElement).value) || 0;
      item.y = Number((document.getElementById("propY") as HTMLInputElement).value) || 0;
      item.w = Math.max(20, Number((document.getElementById("propW") as HTMLInputElement).value) || 20);
      item.h = Math.max(20, Number((document.getElementById("propH") as HTMLInputElement).value) || 20);
      item.z = Number((document.getElementById("propZ") as HTMLInputElement).value) || 0;
      item.padding = Math.max(0, Number((document.getElementById("propPadding") as HTMLInputElement).value) || 0);
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
      gridH: Number(gridH.value) || 32,
      gridV: Number(gridV.value) || 16,
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
  const ids = ["propX", "propY", "propW", "propH", "propZ", "propPadding"];
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
