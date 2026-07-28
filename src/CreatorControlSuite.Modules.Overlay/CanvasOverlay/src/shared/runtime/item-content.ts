import type { LayoutItem } from "../types";
import { createItemContent as createBuiltInContent } from "./core-functions";
import { paintItemContent } from "./paint-item";
import { getRegisteredWidget } from "../extensions/registry";

export { paintItemContent } from "./paint-item";
export type { PaintItemOptions } from "./paint-item";

export function createItemContent(item: LayoutItem): HTMLElement {
  const custom = getRegisteredWidget(item.type);
  if (custom) {
    return custom.create(item);
  }
  return createBuiltInContent(item);
}

export function applyItemBox(wrapper: HTMLElement, item: LayoutItem): void {
  wrapper.style.left = (item.x || 0) + "px";
  wrapper.style.top = (item.y || 0) + "px";
  wrapper.style.width = (item.w || 100) + "px";
  wrapper.style.height = (item.h || 100) + "px";
  wrapper.style.zIndex = String(item.z || 0);
  wrapper.style.transform = item.rotation ? `rotate(${item.rotation}deg)` : "";
  const pad = Math.max(0, Number(item.padding) || 0);
  wrapper.style.padding = pad > 0 ? `${pad}px` : "";
}
