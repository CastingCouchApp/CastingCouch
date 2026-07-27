import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

const BASE_FONTS = [
  "Segoe UI, system-ui, sans-serif",
  "Arial, Helvetica, sans-serif",
  "Georgia, serif",
  "Times New Roman, Times, serif",
  "Courier New, monospace",
  "Impact, Haettenschweiler, sans-serif",
  "Trebuchet MS, sans-serif",
  "Verdana, Geneva, sans-serif",
  "system-ui, sans-serif"
];

function fontChoices(): string[] {
  const pack = (window as unknown as { __ccsPackFonts?: string[] }).__ccsPackFonts || [];
  return [...BASE_FONTS, ...pack];
}

export function fontProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: string
): HTMLElement {
  const wrap = document.createElement("label");
  wrap.className = "ccs-font-prop";
  wrap.textContent = label;

  const current = String((item.props && item.props[key]) ?? fallback);
  const select = document.createElement("select");
  select.className = "ccs-font-select";

  const choices = fontChoices();
  if (current && !choices.includes(current)) {
    choices.unshift(current);
  }

  for (const font of choices) {
    const opt = document.createElement("option");
    opt.value = font;
    opt.textContent = font.split(",")[0].trim();
    if (font === current) opt.selected = true;
    select.appendChild(opt);
  }

  if (!current && fallback) {
    select.value = choices.includes(fallback) ? fallback : choices[0] || "";
  }

  select.addEventListener("change", () => {
    ctx.commitProp(item, (live) => {
      live.props[key] = select.value || fallback;
    });
  });

  wrap.appendChild(select);
  return wrap;
}
