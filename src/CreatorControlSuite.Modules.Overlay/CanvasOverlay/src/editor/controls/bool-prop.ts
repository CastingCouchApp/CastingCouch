import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

export function boolProp(key: string, label: string, item: LayoutItem, ctx: EditorContext): HTMLLabelElement {
  const wrap = document.createElement("label");
  const checked = item.props && (item.props as Record<string, unknown>)[key] !== false;
  wrap.innerHTML = `<span><input type="checkbox" data-prop="${key}" ${checked ? "checked" : ""}/> ${label}</span>`;
  wrap.querySelector("input")!.addEventListener("change", (e) => {
    ctx.commitProp(item, (live) => {
      live.props[key] = (e.target as HTMLInputElement).checked;
    });
  });
  return wrap;
}
