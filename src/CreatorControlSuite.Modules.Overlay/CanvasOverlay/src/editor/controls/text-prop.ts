import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

export function textProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: string
): HTMLLabelElement {
  const wrap = document.createElement("label");
  wrap.textContent = label;
  const input = document.createElement("input");
  input.type = "text";
  const props = item.props as Record<string, unknown> | undefined;
  input.value = (props && props[key] != null ? String(props[key]) : fallback) || fallback;
  input.addEventListener("change", () => {
    ctx.commitProp(item, (live) => {
      live.props[key] = input.value;
    });
  });
  wrap.appendChild(input);
  return wrap;
}
