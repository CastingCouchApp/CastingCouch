export function connectWs(onEvent: (evt: Record<string, unknown>) => void): WebSocket {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  const ws = new WebSocket(`${proto}//${location.host}/ws`);
  ws.addEventListener("message", (msg) => {
    try {
      onEvent(JSON.parse(msg.data as string) as Record<string, unknown>);
    } catch { /* ignore */ }
  });
  ws.addEventListener("close", () => {
    setTimeout(() => connectWs(onEvent), 1500);
  });
  return ws;
}
