import type { LayoutItem } from "../types";
import { createItemContent as createBuiltInContent } from "./core-functions";
import { getRegisteredWidget } from "../extensions/registry";

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
}
