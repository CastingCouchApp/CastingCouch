import type { CreateRuntime } from "../../shared/types";

export function createSaveScheduler(
  runtime: CreateRuntime,
  instanceId: string,
  saveStatus: HTMLElement,
  getWs: () => WebSocket | null
): { scheduleSave: () => void } {
  let saveTimer: ReturnType<typeof setTimeout> | null = null;

  function scheduleSave(): void {
    saveStatus.textContent = "Speichern…";
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(saveNow, 400);
  }

  async function saveNow(): Promise<void> {
    if (!instanceId) {
      saveStatus.textContent = "Keine Instanz-ID";
      return;
    }
    const layout = runtime.getLayout();
    try {
      const res = await fetch("/layout/" + encodeURIComponent(instanceId), {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(layout)
      });
      if (!res.ok) throw new Error("HTTP " + res.status);
      const ws = getWs();
      if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
          source: "editor",
          type: "editor.layout.set",
          data: { instanceId, layout: JSON.stringify(layout) }
        }));
      }
      saveStatus.textContent = "Gespeichert " + new Date().toLocaleTimeString();
    } catch (err) {
      saveStatus.textContent = "Fehler: " + ((err as Error) && (err as Error).message ? (err as Error).message : String(err));
    }
  }

  return { scheduleSave };
}
