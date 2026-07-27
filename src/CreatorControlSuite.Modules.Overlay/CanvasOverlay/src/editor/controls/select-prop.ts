import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

export interface SelectOption {
  value: string;
  label: string;
}

export function selectProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  options: SelectOption[],
  fallback: string,
  customApply?: (live: LayoutItem, value: string) => void
): HTMLLabelElement {
  const wrap = document.createElement("label");
  wrap.textContent = label;
  const select = document.createElement("select");
  const props = item.props as Record<string, unknown> | undefined;
  const current = props && props[key] != null ? String(props[key]) : String(fallback);
  for (const entry of options) {
    const opt = document.createElement("option");
    opt.value = entry.value;
    opt.textContent = entry.label;
    if (opt.value === current) {
      opt.selected = true;
    }
    select.appendChild(opt);
  }
  select.addEventListener("change", () => {
    ctx.commitProp(item, (live) => {
      if (typeof customApply === "function") {
        customApply(live, select.value);
      } else {
        live.props[key] = select.value;
      }
    });
  });
  wrap.appendChild(select);
  return wrap;
}
