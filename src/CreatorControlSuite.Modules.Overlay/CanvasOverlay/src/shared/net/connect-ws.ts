export interface ConnectWsOptions {
  /** Called on every successful open (initial + reconnect). */
  onOpen?: (ws: WebSocket) => void;
  /** Always points at the current socket (updates after reconnect). */
  onSocket?: (ws: WebSocket) => void;
  /** Base retry delay in ms (default 1500). Grows with backoff. */
  retryDelayMs?: number;
  /** Cap for retry delay (default 15000). */
  maxRetryDelayMs?: number;
}

type StoppableSocket = WebSocket & { __ccsStopReconnect?: () => void };

/**
 * Overlay realtime socket with automatic reconnect.
 * OBS Browser Sources drop WS often when the scene is hidden or the app restarts;
 * callers should reload chat (and similar state) from `onOpen`.
 */
export function connectWs(
  onEvent: (evt: Record<string, unknown>) => void,
  options?: ConnectWsOptions
): WebSocket {
  const retryDelayMs = Math.max(250, options?.retryDelayMs ?? 1500);
  const maxRetryDelayMs = Math.max(retryDelayMs, options?.maxRetryDelayMs ?? 15000);
  let attempt = 0;
  let closedIntentionally = false;
  let retryTimer: ReturnType<typeof setTimeout> | null = null;
  let current: WebSocket | null = null;

  function clearRetry(): void {
    if (retryTimer != null) {
      clearTimeout(retryTimer);
      retryTimer = null;
    }
  }

  function stopReconnect(): void {
    closedIntentionally = true;
    clearRetry();
  }

  function scheduleReconnect(): void {
    if (closedIntentionally || retryTimer != null) return;
    const exp = Math.min(maxRetryDelayMs, retryDelayMs * Math.pow(1.6, attempt));
    attempt += 1;
    const jitter = Math.floor(Math.random() * 250);
    retryTimer = setTimeout(() => {
      retryTimer = null;
      open();
    }, exp + jitter);
  }

  function bindStop(ws: WebSocket): void {
    (ws as StoppableSocket).__ccsStopReconnect = stopReconnect;
  }

  function open(): WebSocket {
    clearRetry();
    const proto = location.protocol === "https:" ? "wss:" : "ws:";
    const ws = new WebSocket(`${proto}//${location.host}/ws`);
    current = ws;
    bindStop(ws);
    options?.onSocket?.(ws);

    ws.addEventListener("open", () => {
      attempt = 0;
      options?.onOpen?.(ws);
    });

    ws.addEventListener("message", (msg) => {
      try {
        onEvent(JSON.parse(String(msg.data)) as Record<string, unknown>);
      } catch {
        /* ignore malformed frames */
      }
    });

    ws.addEventListener("close", () => {
      if (current === ws) current = null;
      scheduleReconnect();
    });

    ws.addEventListener("error", () => {
      // CEF/OBS often signals error then close; force close so reconnect runs.
      try {
        if (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN) {
          ws.close();
        }
      } catch {
        /* ignore */
      }
    });

    return ws;
  }

  return open();
}

/** Stop reconnect loops started by {@link connectWs}. */
export function stopWsReconnect(ws: WebSocket | null | undefined): void {
  if (!ws) return;
  const stop = (ws as StoppableSocket).__ccsStopReconnect;
  if (typeof stop === "function") stop();
  try {
    ws.close();
  } catch {
    /* ignore */
  }
}
