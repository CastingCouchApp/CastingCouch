import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

export interface TextPropOptions {
  multiline?: boolean;
  rows?: number;
}

export function textProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: string,
  opts?: TextPropOptions
): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "ccs-prop-row ccs-text-prop" + (opts?.multiline ? " ccs-text-prop-multiline" : "");

  const title = document.createElement("span");
  title.className = "ccs-prop-row-label";
  title.textContent = label;

  const props = item.props as Record<string, unknown> | undefined;
  const value = (props && props[key] != null ? String(props[key]) : fallback) || fallback;

  const input: HTMLInputElement | HTMLTextAreaElement = opts?.multiline
    ? document.createElement("textarea")
    : document.createElement("input");

  if (!opts?.multiline) {
    (input as HTMLInputElement).type = "text";
  } else {
    (input as HTMLTextAreaElement).rows = Math.max(2, opts.rows || 4);
  }
  input.className = "ccs-prop-row-control";
  input.value = value;
  input.addEventListener("change", () => {
    ctx.commitProp(item, (live) => {
      live.props[key] = input.value;
    });
  });

  wrap.appendChild(title);
  wrap.appendChild(input);
  return wrap;
}
