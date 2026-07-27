(function () {
  "use strict";
  const instanceId = (location.pathname.split("/").filter(Boolean).pop() || "").trim();
  const runtime = CcsCanvas.createRuntime({
    root: document.getElementById("root"),
    editing: false,
    center: false,
    instanceId
  });

  // View fills OBS browser source 1:1 to canvas size via CSS scale
  async function boot() {
    if (instanceId) {
      try {
        runtime.setLayout(await CcsCanvas.fetchJson("/layout/" + encodeURIComponent(instanceId)));
      } catch (_) {
        runtime.setLayout({ ...CcsCanvas.DEFAULT_LAYOUT, items: [] });
      }
    }
    try {
      runtime.setData(await CcsCanvas.fetchJson("/data/overlay-data.json"));
    } catch (_) { /* ignore */ }
    try {
      runtime.setChatConfig(await CcsCanvas.fetchJson("/chat/config"));
    } catch (_) { /* ignore */ }
    await runtime.loadChatHistory();
    CcsCanvas.connectWs((evt) => runtime.handleRealtime(evt));
    setInterval(async () => {
      try {
        runtime.setData(await CcsCanvas.fetchJson("/data/overlay-data.json"));
      } catch (_) { /* ignore */ }
    }, 1500);
  }
  boot();
})();
