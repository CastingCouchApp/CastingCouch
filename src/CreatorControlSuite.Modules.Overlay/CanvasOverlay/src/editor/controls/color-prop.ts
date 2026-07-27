import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

const SWATCHES = [
  "#000000", "#ffffff", "#ff0000", "#00ff00", "#0000ff",
  "#ffff00", "#ff00ff", "#00ffff", "#808080", "#ff7a00", "#ffb36b", "#1a1a1a"
];

function toHex6(value: string, fallback: string): string {
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

export function colorProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: string
): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "ccs-color-prop";
  const title = document.createElement("span");
  title.className = "ccs-color-prop-label";
  title.textContent = label;
  wrap.appendChild(title);

  const row = document.createElement("div");
  row.className = "ccs-color-prop-row";

  const current = String((item.props && item.props[key]) ?? fallback);
  const picker = document.createElement("input");
  picker.type = "color";
  picker.value = toHex6(current, fallback);
  picker.title = "Farbe wählen";

  const text = document.createElement("input");
  text.type = "text";
  text.value = current;
  text.placeholder = "#RRGGBB / css";

  const applyValue = (value: string, livePreview: boolean) => {
    const write = (live: LayoutItem) => {
      live.props[key] = value;
    };
    if (livePreview && ctx.previewProp) {
      ctx.previewProp(item, write);
    } else {
      ctx.commitProp(item, write);
    }
  };

  // input = HSV-Ziehen: Canvas live, Props-Panel NICHT neu bauen
  picker.addEventListener("input", () => {
    text.value = picker.value;
    applyValue(picker.value, true);
  });
  // change = Picker geschlossen: speichern + Panel sync ok
  picker.addEventListener("change", () => {
    text.value = picker.value;
    applyValue(picker.value, false);
  });
  text.addEventListener("change", () => {
    const next = text.value.trim() || fallback;
    picker.value = toHex6(next, fallback);
    applyValue(next, false);
  });

  row.appendChild(picker);
  row.appendChild(text);
  wrap.appendChild(row);

  const swatchRow = document.createElement("div");
  swatchRow.className = "ccs-color-swatches";
  for (const hex of SWATCHES) {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "ccs-color-swatch";
    btn.style.background = hex;
    btn.title = hex;
    btn.addEventListener("click", () => {
      text.value = hex;
      picker.value = hex;
      applyValue(hex, false);
    });
    swatchRow.appendChild(btn);
  }
  wrap.appendChild(swatchRow);
  return wrap;
}
