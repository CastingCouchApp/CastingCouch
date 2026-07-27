import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "../props/context";

export interface NumPropOptions {
  step?: number;
  min?: number;
  max?: number;
}

function inferRange(key: string, fallback: number, step: number): { min: number; max: number } {
  const k = key.toLowerCase();
  if (k.includes("percent") || k.endsWith("pct")) {
    return { min: 0, max: 100 };
  }
  if (
    k.includes("opacity") ||
    k.includes("intensity") ||
    k === "opacity" ||
    (fallback >= 0 && fallback <= 1 && step <= 0.1)
  ) {
    return { min: 0, max: 1 };
  }
  if (k.includes("fontsize") || k.includes("iconsize")) {
    return { min: 8, max: 200 };
  }
  if (k.includes("durationms") || k.includes("duration")) {
    return { min: 0, max: Math.max(10000, fallback * 3) };
  }
  if (k.includes("blur") || k.includes("spread")) {
    return { min: 0, max: 120 };
  }
  if (k.includes("letterspacing")) {
    return { min: -20, max: 40 };
  }
  if (k.includes("lineheight")) {
    return { min: 0.5, max: 3 };
  }
  if (k.includes("speed") || k.includes("density") || k.includes("tempo")) {
    return { min: 0.1, max: 5 };
  }
  if (k.includes("padding") || k.includes("gap") || k.includes("radius") || k.includes("size")) {
    return { min: 0, max: Math.max(120, Math.ceil(fallback * 4) || 120) };
  }
  if (k.includes("maxlines") || k.includes("z")) {
    return { min: 0, max: Math.max(200, Math.ceil(fallback * 3) || 200) };
  }
  const span = Math.max(Math.abs(fallback) * 3, 10);
  return {
    min: fallback < 0 ? -span : 0,
    max: Math.max(span, fallback + span)
  };
}

function resolveStep(fallback: number, step?: number): number {
  if (step != null) return step;
  if (!Number.isInteger(fallback)) {
    if (Math.abs(fallback) <= 1) return 0.01;
    return 0.05;
  }
  return 1;
}

export function numProp(
  key: string,
  label: string,
  item: LayoutItem,
  ctx: EditorContext,
  fallback: number,
  stepOrOpts?: number | NumPropOptions
): HTMLElement {
  const opts: NumPropOptions = typeof stepOrOpts === "number"
    ? { step: stepOrOpts }
    : (stepOrOpts || {});
  const step = resolveStep(fallback, opts.step);
  const inferred = inferRange(key, fallback, step);
  const min = opts.min != null ? opts.min : inferred.min;
  const max = opts.max != null ? opts.max : inferred.max;

  const props = item.props as Record<string, unknown> | undefined;
  let value = props && props[key] != null ? Number(props[key]) : fallback;
  if (!Number.isFinite(value)) value = fallback;
  value = Math.min(max, Math.max(min, value));

  const wrap = document.createElement("div");
  wrap.className = "ccs-num-prop";

  const head = document.createElement("div");
  head.className = "ccs-num-prop-head";
  const title = document.createElement("span");
  title.textContent = label;
  head.appendChild(title);
  wrap.appendChild(head);

  const row = document.createElement("div");
  row.className = "ccs-num-prop-row";

  const slider = document.createElement("input");
  slider.type = "range";
  slider.className = "ccs-num-slider";
  slider.min = String(min);
  slider.max = String(max);
  slider.step = String(step);
  slider.value = String(value);

  const input = document.createElement("input");
  input.type = "number";
  input.className = "ccs-num-input";
  input.min = String(min);
  input.max = String(max);
  input.step = String(step);
  input.value = String(value);

  const applyValue = (next: number, livePreview: boolean) => {
    const clamped = Math.min(max, Math.max(min, next));
    slider.value = String(clamped);
    input.value = String(clamped);
    const write = (live: LayoutItem) => {
      live.props[key] = clamped;
    };
    if (livePreview && ctx.previewProp) {
      ctx.previewProp(item, write);
    } else {
      ctx.commitProp(item, write);
    }
  };

  slider.addEventListener("input", () => {
    applyValue(Number(slider.value), true);
  });
  slider.addEventListener("change", () => {
    applyValue(Number(slider.value), false);
  });
  input.addEventListener("input", () => {
    const n = Number(input.value);
    if (!Number.isFinite(n)) return;
    slider.value = String(Math.min(max, Math.max(min, n)));
    applyValue(n, true);
  });
  input.addEventListener("change", () => {
    const n = Number(input.value);
    applyValue(Number.isFinite(n) ? n : fallback, false);
  });

  row.appendChild(slider);
  row.appendChild(input);
  wrap.appendChild(row);
  return wrap;
}
