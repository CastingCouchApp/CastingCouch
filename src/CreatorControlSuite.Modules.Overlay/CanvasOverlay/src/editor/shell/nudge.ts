import type { CreateRuntime, LayoutItem } from "../../shared/types";

export function nudgeSelected(
  runtime: CreateRuntime,
  dx: number,
  dy: number,
  scheduleSave: () => void
): boolean {
  const id = runtime.getSelectedId();
  if (!id) return false;
  const item = (runtime.getLayout().items || []).find((i) => i.id === id);
  if (!item || item.locked) return false;
  item.x = (item.x || 0) + dx;
  item.y = (item.y || 0) + dy;
  runtime.renderItems();
  runtime.select(item.id);
  scheduleSave();
  return true;
}

export function nudgeDeltaForKey(key: string, shiftKey: boolean): { dx: number; dy: number } | null {
  const step = shiftKey ? 10 : 1;
  switch (key) {
    case "ArrowLeft":
      return { dx: -step, dy: 0 };
    case "ArrowRight":
      return { dx: step, dy: 0 };
    case "ArrowUp":
      return { dx: 0, dy: -step };
    case "ArrowDown":
      return { dx: 0, dy: step };
    default:
      return null;
  }
}
