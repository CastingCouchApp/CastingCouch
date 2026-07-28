(function () {
  "use strict";
  const instanceId = (location.pathname.split("/").filter(Boolean).pop() || "").trim();
  const runtime = CcsCanvas.createRuntime({
    root: document.getElementById("root")!,
    editing: false,
    center: false,
    instanceId
  });

  async function refreshChat(): Promise<void> {
    try {
      runtime.setChatConfig(await CcsCanvas.fetchJson("/chat/config"));
    } catch { /* optional */ }
    await runtime.loadChatHistory();
  }

  async function boot(): Promise<void> {
    await CcsCanvas.loadExtensions();
    if (instanceId) {
      try {
        runtime.setLayout(await CcsCanvas.fetchJson("/layout/" + encodeURIComponent(instanceId)) as never);
      } catch {
        runtime.setLayout({ ...CcsCanvas.DEFAULT_LAYOUT, items: [] });
      }
    }
    try {
      runtime.setData(await CcsCanvas.fetchJson("/data/overlay-data.json") as Record<string, unknown>);
    } catch { /* ignore */ }

    CcsCanvas.connectWs(
      (evt) => runtime.handleRealtime(evt),
      {
        onOpen: () => {
          void refreshChat();
        }
      }
    );

    setInterval(async () => {
      try {
        runtime.setData(await CcsCanvas.fetchJson("/data/overlay-data.json") as Record<string, unknown>);
      } catch { /* ignore */ }
    }, 1500);
  }
  void boot();
})();
