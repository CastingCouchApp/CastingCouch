export interface LayoutItem {
  id: string;
  kind: string;
  type: string;
  x: number;
  y: number;
  w: number;
  h: number;
  z: number;
  /** Uniform content inset in px (box-sizing: border-box). */
  padding?: number;
  rotation?: number;
  locked?: boolean;
  props: Record<string, unknown>;
  effects?: EffectInstance[];
  animations?: AnimationInstance[];
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
  /** Where the effect is painted: item box/container, or drawn content (shape/text). Default: box. */
  target?: "box" | "content";
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
  /** Where this effect can paint. Default: box only. */
  targets?: Array<"box" | "content">;
  apply: (
    layer: HTMLElement,
    effect: EffectInstance,
    item: LayoutItem,
    wrapper?: HTMLElement
  ) => void;
}

export interface AnimationInstance {
  id?: string;
  type: string;
  enabled?: boolean;
  settings?: Record<string, unknown>;
  [key: string]: unknown;
}

export interface AnimationField {
  key: string;
  kind: "color" | "number" | "bool" | "text" | "select";
  label: string;
  fallback?: unknown;
  step?: number;
  min?: number;
  max?: number;
  options?: Array<{ value: string; label: string }>;
}

export interface AnimationStrategy {
  type?: string;
  label?: string;
  defaults?: Record<string, unknown>;
  fields?: AnimationField[];
  /** Returns CSS animation shorthand fragment, or void if only vars/classes were set. */
  apply: (
    target: HTMLElement,
    animation: AnimationInstance,
    item: LayoutItem
  ) => string | void;
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
  /** Called after each full canvas rebuild (setLayout / renderItems). */
  onAfterRender?: (ctx: { canvas: HTMLElement }) => void;
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
  createItemContent: (item: LayoutItem) => HTMLElement;
  paintItemContent: (
    el: HTMLElement,
    item: LayoutItem,
    data?: Record<string, unknown> | null,
    chatConfig?: unknown,
    options?: { seedDemo?: boolean; [key: string]: unknown }
  ) => void;
  fetchJson: (url: string) => Promise<unknown>;
  connectWs: (
    onEvent: (evt: Record<string, unknown>) => void,
    options?: {
      onOpen?: (ws: WebSocket) => void;
      onSocket?: (ws: WebSocket) => void;
      retryDelayMs?: number;
      maxRetryDelayMs?: number;
    }
  ) => WebSocket;
  WIDGET_DEFAULTS: Record<string, WidgetDefaults>;
  SHAPE_DEFAULTS: Record<string, WidgetDefaults>;
  SCENE_BG_PRESETS: Record<string, Record<string, unknown>>;
  CARD_FRAME_SIZE_PRESETS: Record<string, { w: number; h: number; label: string }>;
  CARD_FRAME_VARIANTS: string[];
  FRAME_MODES: readonly string[];
  FRAME_MODE_LABELS: Record<string, string>;
  MUSIC_VARIANTS: readonly string[];
  MUSIC_VARIANT_LABELS: Record<string, string>;
  MUSIC_SIZE_PRESETS: Record<string, { w: number; h: number; label: string; scale?: number }>;
  PARTNER_ROULETTE_TRANSITIONS: readonly string[];
  GOAL_BAR_VARIANTS?: readonly string[];
  GOAL_BAR_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  EVENT_TICKER_VARIANTS?: readonly string[];
  EVENT_TICKER_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  EVENT_TICKER_SOURCES?: readonly string[];
  VIEWER_COUNT_VARIANTS?: readonly string[];
  VIEWER_COUNT_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  LOWER_THIRD_VARIANTS?: readonly string[];
  LOWER_THIRD_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  QR_CODE_VARIANTS?: readonly string[];
  QR_CODE_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  BRB_PANEL_VARIANTS?: readonly string[];
  BRB_PANEL_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  BRB_PANEL_MODES?: readonly string[];
  ANNOUNCEMENT_BAR_VARIANTS?: readonly string[];
  ANNOUNCEMENT_BAR_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  BUBATZ_CANTINA_VARIANTS?: readonly string[];
  BUBATZ_CANTINA_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  BUBATZ_CANTINA_MODES?: readonly string[];
  FRUPPIS_LANDADEL_VARIANTS?: readonly string[];
  FRUPPIS_LANDADEL_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  ANIMATED_BACKGROUND_VARIANTS?: readonly string[];
  ANIMATED_BACKGROUND_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  ANIMATED_BACKGROUND_VARIANT_LABELS?: Record<string, string>;
  DIVIDER_VARIANTS?: readonly string[];
  DIVIDER_STYLES?: readonly string[];
  DIVIDER_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  CAM_RING_VARIANTS?: readonly string[];
  CAM_RING_SIZE_PRESETS?: Record<string, { w: number; h: number; label: string }>;
  STICKER_PRESETS?: readonly string[];
  STICKER_VARIANTS?: readonly string[];
  DEFAULT_LAYOUT: Layout;
  uid: () => string;
  registerWidget: (type: string, handlers: WidgetHandlers) => void;
  registerEffect: (type: string, strategy: EffectStrategy) => void;
  listEffectTypes: () => string[];
  EFFECT_STRATEGIES: Record<string, EffectStrategy>;
  registerAnimation: (type: string, strategy: AnimationStrategy) => void;
  listAnimationTypes: () => string[];
  ANIMATION_STRATEGIES: Record<string, AnimationStrategy>;
  extUrl: (packId: string, path: string) => string;
  loadExtensions: () => Promise<Array<{
    id: string;
    name?: string;
    widgets?: Array<{ id?: string; name?: string; entry?: string; css?: string }>;
    effects?: Array<{ id?: string; name?: string; entry?: string; css?: string }>;
    animations?: Array<{ id?: string; name?: string; entry?: string; css?: string }>;
    fonts?: Array<{ family?: string; src?: string; weight?: string; style?: string }>;
  }>>;
}
