import type { LayoutItem } from "../types";

export function prop(item: LayoutItem | null | undefined, key: string, fallback: unknown): unknown {
  const props = item && item.props ? item.props : {};
  return (props as Record<string, unknown>)[key] === undefined ? fallback : (props as Record<string, unknown>)[key];
}
