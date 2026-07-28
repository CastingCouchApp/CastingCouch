import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

export function boolProp(key: string, label: string, item: LayoutItem, ctx: EditorContext): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "ccs-prop-row ccs-bool-prop";

  const title = document.createElement("span");
  title.className = "ccs-prop-row-label";
  title.textContent = label;

  const checked = item.props && (item.props as Record<string, unknown>)[key] !== false;
  const input = document.createElement("input");
  input.type = "checkbox";
  input.className = "ccs-check ccs-bool-input";
  input.dataset.prop = key;
  input.checked = !!checked;
  input.addEventListener("change", () => {
    ctx.commitProp(item, (live) => {
      live.props[key] = input.checked;
    });
  });

  wrap.appendChild(title);
  wrap.appendChild(input);
  return wrap;
}
