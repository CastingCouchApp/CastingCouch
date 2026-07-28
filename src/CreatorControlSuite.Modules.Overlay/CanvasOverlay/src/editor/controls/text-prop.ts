import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

export function textProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: string
): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "ccs-prop-row ccs-text-prop";

  const title = document.createElement("span");
  title.className = "ccs-prop-row-label";
  title.textContent = label;

  const input = document.createElement("input");
  input.type = "text";
  input.className = "ccs-prop-row-control";
  const props = item.props as Record<string, unknown> | undefined;
  input.value = (props && props[key] != null ? String(props[key]) : fallback) || fallback;
  input.addEventListener("change", () => {
    ctx.commitProp(item, (live) => {
      live.props[key] = input.value;
    });
  });

  wrap.appendChild(title);
  wrap.appendChild(input);
  return wrap;
}
