import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

const SWATCHES = [
  "#000000", "#ffffff", "#ff0000", "#00ff00", "#0000ff",
  "#ffff00", "#ff00ff", "#00ffff", "#808080", "#ff7a00", "#ffb36b", "#1a1a1a"
];

export const COLOR_HISTORY_KEY = "ccs-color-history";
export const COLOR_HISTORY_MAX = 12;

export function toHex6(value: string, fallback = "#ffffff"): string {
  const v = (value || "").trim();
  if (/^#[0-9a-fA-F]{6}$/.test(v)) return v.toLowerCase();
  if (/^#[0-9a-fA-F]{3}$/.test(v)) {
    const r = v[1];
    const g = v[2];
    const b = v[3];
    return ("#" + r + r + g + g + b + b).toLowerCase();
  }
  return fallback.startsWith("#") && fallback.length === 7 ? fallback.toLowerCase() : "#ffffff";
}

/** Normalize to #rrggbb when possible; otherwise null (skip history). */
export function normalizeHistoryColor(value: string): string | null {
  const v = (value || "").trim();
  if (/^#[0-9a-fA-F]{6}$/.test(v)) return v.toLowerCase();
  if (/^#[0-9a-fA-F]{3}$/.test(v)) return toHex6(v);
  return null;
}

export function loadColorHistory(): string[] {
  try {
    const raw = localStorage.getItem(COLOR_HISTORY_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    const out: string[] = [];
    for (const entry of parsed) {
      const hex = normalizeHistoryColor(String(entry));
      if (hex && !out.includes(hex)) out.push(hex);
      if (out.length >= COLOR_HISTORY_MAX) break;
    }
    return out;
  } catch {
    return [];
  }
}

export function pushColorHistory(value: string): string[] {
  const hex = normalizeHistoryColor(value);
  if (!hex) return loadColorHistory();
  const next = [hex, ...loadColorHistory().filter((c) => c !== hex)].slice(0, COLOR_HISTORY_MAX);
  try {
    localStorage.setItem(COLOR_HISTORY_KEY, JSON.stringify(next));
  } catch {
    /* ignore quota */
  }
  return next;
}

function makeSwatch(
  hex: string,
  onPick: (hex: string) => void
): HTMLButtonElement {
  const btn = document.createElement("button");
  btn.type = "button";
  btn.className = "ccs-color-swatch";
  btn.style.background = hex;
  btn.title = hex;
  btn.addEventListener("click", () => onPick(hex));
  return btn;
}

export function colorProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: string
): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "ccs-prop-row ccs-color-prop";

  const title = document.createElement("span");
  title.className = "ccs-prop-row-label";
  title.textContent = label;
  wrap.appendChild(title);

  const row = document.createElement("div");
  row.className = "ccs-prop-row-control ccs-color-prop-row";

  const expand = document.createElement("button");
  expand.type = "button";
  expand.className = "ccs-color-swatches-toggle";
  expand.setAttribute("aria-label", "Farbpalette");
  expand.setAttribute("aria-expanded", "false");
  expand.title = "Farbpalette";

  const current = String((item.props && item.props[key]) ?? fallback);
  const picker = document.createElement("input");
  picker.type = "color";
  picker.value = toHex6(current, fallback);
  picker.title = "Farbe wählen";

  const text = document.createElement("input");
  text.type = "text";
  text.className = "ccs-color-text";
  text.value = current;
  text.placeholder = "#RRGGBB / css";

  const swatchPanel = document.createElement("div");
  swatchPanel.className = "ccs-prop-row-extra ccs-color-swatches";
  swatchPanel.hidden = true;

  const historyRow = document.createElement("div");
  historyRow.className = "ccs-color-history";
  historyRow.setAttribute("aria-label", "Zuletzt verwendet");

  const presetRow = document.createElement("div");
  presetRow.className = "ccs-color-presets";

  const pickColor = (hex: string, livePreview: boolean) => {
    text.value = hex;
    picker.value = toHex6(hex, fallback);
    const write = (live: LayoutItem) => {
      live.props[key] = hex;
    };
    if (livePreview && ctx.previewProp) {
      ctx.previewProp(item, write);
    } else {
      ctx.commitProp(item, write);
      pushColorHistory(hex);
      renderHistory();
    }
  };

  function renderHistory(): void {
    historyRow.innerHTML = "";
    const history = loadColorHistory();
    historyRow.hidden = history.length === 0;
    for (const hex of history) {
      historyRow.appendChild(makeSwatch(hex, (c) => pickColor(c, false)));
    }
  }

  for (const hex of SWATCHES) {
    presetRow.appendChild(makeSwatch(hex, (c) => pickColor(c, false)));
  }

  renderHistory();
  swatchPanel.appendChild(historyRow);
  swatchPanel.appendChild(presetRow);

  picker.addEventListener("input", () => {
    text.value = picker.value;
    pickColor(picker.value, true);
  });
  picker.addEventListener("change", () => {
    text.value = picker.value;
    pickColor(picker.value, false);
  });
  text.addEventListener("change", () => {
    const next = text.value.trim() || fallback;
    picker.value = toHex6(next, fallback);
    pickColor(next, false);
  });

  expand.addEventListener("click", () => {
    const open = swatchPanel.hidden;
    swatchPanel.hidden = !open;
    wrap.classList.toggle("ccs-color-prop--open", open);
    expand.setAttribute("aria-expanded", open ? "true" : "false");
    if (open) renderHistory();
  });

  row.appendChild(expand);
  row.appendChild(picker);
  row.appendChild(text);
  wrap.appendChild(row);
  wrap.appendChild(swatchPanel);
  return wrap;
}
