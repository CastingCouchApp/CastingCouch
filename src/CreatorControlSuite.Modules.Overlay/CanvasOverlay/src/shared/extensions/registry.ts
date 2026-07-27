import type { WidgetHandlers } from "../types";

const WIDGET_REGISTRY: Record<string, WidgetHandlers> = {};

export function registerWidget(type: string, handlers: WidgetHandlers): void {
  WIDGET_REGISTRY[type] = handlers;
}

export function getRegisteredWidget(type: string): WidgetHandlers | undefined {
  return WIDGET_REGISTRY[type];
}

export function listRegisteredWidgets(): string[] {
  return Object.keys(WIDGET_REGISTRY);
}
