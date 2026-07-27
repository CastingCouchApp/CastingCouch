export interface LayoutItem {
  id: string;
  kind: string;
  type: string;
  x: number;
  y: number;
  w: number;
  h: number;
  z: number;
  rotation?: number;
  locked?: boolean;
  props: Record<string, unknown>;
  effects?: EffectInstance[];
}

export interface Layout {
  version: number;
  canvasWidth: number;
  canvasHeight: number;
  name?: string;
  items: LayoutItem[];
}

export interface EffectInstance {
  id?: string;
  type: string;
  enabled?: boolean;
  settings?: Record<string, unknown>;
  [key: string]: unknown;
}

export interface EffectField {
  key: string;
  kind: "color" | "number" | "bool" | "text" | "select";
  label: string;
  fallback?: unknown;
  step?: number;
  min?: number;
  max?: number;
  options?: Array<{ value: string; label: string }>;
}

export interface EffectStrategy {
  type?: string;
  label?: string;
  defaults?: Record<string, unknown>;
  fields?: EffectField[];
  apply: (
    layer: HTMLElement,
    effect: EffectInstance,
    item: LayoutItem,
    wrapper?: HTMLElement
  ) => void;
}

export interface WidgetDefaults {
  w: number;
  h: number;
  props: Record<string, unknown>;
}

export interface WidgetHandlers {
  create: (item: LayoutItem) => HTMLElement;
  update?: (el: HTMLElement, item: LayoutItem, data?: Record<string, unknown>, chatConfig?: unknown) => void;
  defaults?: WidgetDefaults;
}

export interface RuntimeOptions {
  root: HTMLElement;
  editing?: boolean;
  center?: boolean;
  instanceId?: string;
  soloType?: string | null;
  layout?: Layout;
  data?: Record<string, unknown>;
  chatConfig?: unknown;
  onSelect?: (item: LayoutItem | null) => void;
  onChange?: (layout: Layout) => void;
}

export interface ItemNode {
  wrapper: HTMLElement;
  content: HTMLElement;
  item: LayoutItem;
}

export interface CreateRuntime {
  canvas: HTMLElement;
  fit: () => void;
  setLayout: (next: Layout | null | undefined, keepSelection?: boolean) => void;
  getLayout: () => Layout;
  setData: (next: Record<string, unknown> | null | undefined) => void;
  getData: () => Record<string, unknown>;
  setChatConfig: (next: unknown) => void;
  loadChatHistory: () => Promise<void>;
  ingestChatHistory: (events: unknown) => void;
  select: (id: string | null) => void;
  getSelectedId: () => string | null;
  handleRealtime: (evt: Record<string, unknown>) => void;
  renderItems: () => void;
  emitChange: () => void;
  itemNodes: Map<string, ItemNode>;
  defaultsFor: (type: string, kind?: string) => WidgetDefaults;
  createItem: (type: string, kind: string | undefined, x: number, y: number) => LayoutItem;
  WIDGET_DEFAULTS: Record<string, WidgetDefaults>;
  SHAPE_DEFAULTS: Record<string, WidgetDefaults>;
  DEFAULT_LAYOUT: Layout;
}

declare global {
  interface Window {
    CcsCanvas: CcsCanvasApi;
  }
}

export interface CcsCanvasApi {
  createRuntime: (options: RuntimeOptions) => CreateRuntime;
  fetchJson: (url: string) => Promise<unknown>;
  connectWs: (onEvent: (evt: Record<string, unknown>) => void) => WebSocket;
  WIDGET_DEFAULTS: Record<string, WidgetDefaults>;
  SHAPE_DEFAULTS: Record<string, WidgetDefaults>;
  SCENE_BG_PRESETS: Record<string, Record<string, unknown>>;
  CARD_FRAME_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }>;
  CARD_FRAME_VARIANTS: string[];
  DEFAULT_LAYOUT: Layout;
  uid: () => string;
  registerWidget: (type: string, handlers: WidgetHandlers) => void;
  registerEffect: (type: string, strategy: EffectStrategy) => void;
  listEffectTypes: () => string[];
  EFFECT_STRATEGIES: Record<string, EffectStrategy>;
  extUrl: (packId: string, path: string) => string;
  loadExtensions: () => Promise<void>;
}
