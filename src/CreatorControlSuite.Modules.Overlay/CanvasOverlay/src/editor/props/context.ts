import type { CreateRuntime, LayoutItem } from "../../shared/types";

export interface EditorContext {
  runtime: CreateRuntime;
  scheduleSave: () => void;
  liveItem: (from?: LayoutItem | null) => LayoutItem | null;
  commitProp: (from: LayoutItem, apply: (live: LayoutItem) => void) => LayoutItem | null;
  /** Canvas live updaten ohne Props-Panel neu aufzubauen (z. B. Color-Picker HSV). */
  previewProp: (from: LayoutItem, apply: (live: LayoutItem) => void) => LayoutItem | null;
}
