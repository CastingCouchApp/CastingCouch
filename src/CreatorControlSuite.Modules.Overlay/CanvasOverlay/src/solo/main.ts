(function () {
  "use strict";

  const parts = location.pathname.split("/").filter(Boolean);
  let type = "online";
  if (parts[0] === "w") {
    if (parts[1] === "shape" && parts[2]) {
      type = decodeURIComponent(parts[2]);
    } else if (parts[1]) {
      type = decodeURIComponent(parts[1]);
    }
  }

  const params = new URLSearchParams(location.search);
  let props: Record<string, unknown> = {};
  if (params.get("props")) {
    try {
      props = JSON.parse(decodeURIComponent(params.get("props")!));
    } catch {
      try {
        props = JSON.parse(atob(params.get("props")!));
      } catch { /* ignore */ }
    }
  }

  const kind = (type === "frame" || type.startsWith("frame.") || type.startsWith("shape.") || type === "shape.vignette" || type === "shape.scene-bg") ? "shape" : "widget";
  const def = kind === "shape"
    ? (CcsCanvas.SHAPE_DEFAULTS[type] || { w: 400, h: 300, props: {} })
    : (CcsCanvas.WIDGET_DEFAULTS[type] || { w: 400, h: 200, props: {} });

  const item = {
    id: "solo",
    kind,
    type,
    x: 0,
    y: 0,
    w: Number(params.get("w")) || def.w,
    h: Number(params.get("h")) || def.h,
    z: 1,
    effects: [] as unknown[],
    props: { ...(def.props || {}), ...props }
  };

  const layout = {
    version: 1,
    canvasWidth: item.w,
    canvasHeight: item.h,
    items: [item]
  };

  const runtime = CcsCanvas.createRuntime({
    root: document.getElementById("root")!,
    editing: false,
    center: true,
    layout: layout as never
  });
  runtime.setLayout(layout as never);

  async function refreshData(): Promise<void> {
    try {
      runtime.setData(await CcsCanvas.fetchJson("/data/overlay-data.json") as Record<string, unknown>);
    } catch { /* ignore */ }
  }

  CcsCanvas.connectWs((evt) => runtime.handleRealtime(evt));
  void refreshData();
  setInterval(refreshData, 1500);

  async function loadChatConfig(): Promise<void> {
    try {
      runtime.setChatConfig(await CcsCanvas.fetchJson("/chat/config"));
    } catch { /* ignore */ }
  }

  void CcsCanvas.loadExtensions().then(() => {
    void Promise.all([loadChatConfig(), runtime.loadChatHistory()]);
  });
})();
