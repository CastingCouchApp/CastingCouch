// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { connectWs, stopWsReconnect } from "../src/shared/net/connect-ws";

type Listener = (ev?: unknown) => void;

class FakeWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances: FakeWebSocket[] = [];

  readyState = FakeWebSocket.CONNECTING;
  url: string;
  private listeners = new Map<string, Listener[]>();

  constructor(url: string) {
    this.url = url;
    FakeWebSocket.instances.push(this);
  }

  addEventListener(type: string, fn: Listener): void {
    const list = this.listeners.get(type) || [];
    list.push(fn);
    this.listeners.set(type, list);
  }

  close(): void {
    if (this.readyState === FakeWebSocket.CLOSED) return;
    this.readyState = FakeWebSocket.CLOSED;
    this.emit("close");
  }

  emit(type: string, ev?: unknown): void {
    for (const fn of this.listeners.get(type) || []) fn(ev);
  }

  openNow(): void {
    this.readyState = FakeWebSocket.OPEN;
    this.emit("open");
  }

  message(data: unknown): void {
    this.emit("message", { data: typeof data === "string" ? data : JSON.stringify(data) });
  }
}

describe("connectWs reconnect", () => {
  beforeEach(() => {
    FakeWebSocket.instances = [];
    vi.stubGlobal("WebSocket", FakeWebSocket as unknown as typeof WebSocket);
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("reconnects after close and notifies onOpen/onSocket", () => {
    const onEvent = vi.fn();
    const onOpen = vi.fn();
    const onSocket = vi.fn();

    connectWs(onEvent, { onOpen, onSocket, retryDelayMs: 1000, maxRetryDelayMs: 1000 });

    expect(FakeWebSocket.instances).toHaveLength(1);
    expect(onSocket).toHaveBeenCalledTimes(1);
    FakeWebSocket.instances[0].openNow();
    expect(onOpen).toHaveBeenCalledTimes(1);

    FakeWebSocket.instances[0].close();
    expect(FakeWebSocket.instances).toHaveLength(1);

    vi.advanceTimersByTime(1300);
    expect(FakeWebSocket.instances).toHaveLength(2);
    expect(onSocket).toHaveBeenCalledTimes(2);

    FakeWebSocket.instances[1].openNow();
    expect(onOpen).toHaveBeenCalledTimes(2);
  });

  it("forwards parsed messages", () => {
    const onEvent = vi.fn();
    connectWs(onEvent, { retryDelayMs: 1000 });
    FakeWebSocket.instances[0].openNow();
    FakeWebSocket.instances[0].message({ type: "app.ws.hello" });
    expect(onEvent).toHaveBeenCalledWith({ type: "app.ws.hello" });
  });

  it("stopWsReconnect prevents further reconnect attempts", () => {
    const ws = connectWs(() => undefined, { retryDelayMs: 500, maxRetryDelayMs: 500 });
    stopWsReconnect(ws);
    expect(FakeWebSocket.instances).toHaveLength(1);
    vi.advanceTimersByTime(5000);
    expect(FakeWebSocket.instances).toHaveLength(1);
  });
});
