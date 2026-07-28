import type { CreateRuntime, LayoutItem } from "../../shared/types";
import { uid } from "../../shared/utils/format";

export type EditorCommand =
  | "delete"
  | "duplicate"
  | "toggleLock"
  | "bringFront"
  | "sendBack"
  | "layerUp"
  | "layerDown";

function selected(runtime: CreateRuntime): LayoutItem | null {
  const id = runtime.getSelectedId();
  if (!id) return null;
  return (runtime.getLayout().items || []).find((i) => i.id === id) || null;
}

function refresh(runtime: CreateRuntime, itemId: string | null, scheduleSave: () => void): void {
  runtime.renderItems();
  if (itemId) runtime.select(itemId);
  else runtime.select(null);
  scheduleSave();
}

export function runEditorCommand(
  command: EditorCommand,
  runtime: CreateRuntime,
  scheduleSave: () => void
): boolean {
  const item = selected(runtime);
  if (!item) return false;
  const layout = runtime.getLayout();
  const items = layout.items || [];

  switch (command) {
    case "delete": {
      if (item.locked) return false;
      layout.items = items.filter((i) => i.id !== item.id);
      runtime.setLayout(layout);
      scheduleSave();
      return true;
    }
    case "duplicate": {
      const copy: LayoutItem = {
        ...item,
        id: uid(),
        x: (item.x || 0) + 20,
        y: (item.y || 0) + 20,
        z: Math.max(0, ...items.map((i) => i.z || 0)) + 1,
        locked: false,
        props: { ...(item.props || {}) },
        effects: Array.isArray(item.effects)
          ? item.effects.map((e) => ({ ...e, settings: { ...(e.settings || {}) } }))
          : [],
        animations: Array.isArray(item.animations)
          ? item.animations.map((a) => ({ ...a, settings: { ...(a.settings || {}) } }))
          : []
      };
      layout.items = [...items, copy];
      runtime.setLayout(layout, true);
      refresh(runtime, copy.id, scheduleSave);
      return true;
    }
    case "toggleLock": {
      item.locked = !item.locked;
      refresh(runtime, item.id, scheduleSave);
      return true;
    }
    case "bringFront": {
      const maxZ = Math.max(0, ...items.map((i) => i.z || 0));
      item.z = maxZ + 1;
      refresh(runtime, item.id, scheduleSave);
      return true;
    }
    case "sendBack": {
      const zs = items.map((i) => i.z || 0);
      const minZ = zs.length ? Math.min(...zs) : 0;
      item.z = minZ - 1;
      refresh(runtime, item.id, scheduleSave);
      return true;
    }
    case "layerUp": {
      const sorted = items.slice().sort((a, b) => (a.z || 0) - (b.z || 0));
      const idx = sorted.findIndex((i) => i.id === item.id);
      if (idx < 0 || idx >= sorted.length - 1) return false;
      const above = sorted[idx + 1];
      const z = item.z || 0;
      item.z = above.z || 0;
      above.z = z;
      refresh(runtime, item.id, scheduleSave);
      return true;
    }
    case "layerDown": {
      const sorted = items.slice().sort((a, b) => (a.z || 0) - (b.z || 0));
      const idx = sorted.findIndex((i) => i.id === item.id);
      if (idx <= 0) return false;
      const below = sorted[idx - 1];
      const z = item.z || 0;
      item.z = below.z || 0;
      below.z = z;
      refresh(runtime, item.id, scheduleSave);
      return true;
    }
    default:
      return false;
  }
}
