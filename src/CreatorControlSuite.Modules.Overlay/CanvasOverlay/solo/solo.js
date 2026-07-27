(function () {
  "use strict";

  const parts = location.pathname.split("/").filter(Boolean);
  // /w/{type} or /w/shape/{shapeId}
  let type = "online";
  if (parts[0] === "w") {
    if (parts[1] === "shape" && parts[2]) {
      type = decodeURIComponent(parts.slice(2).join("."));
      // Prefer literal segment: /w/shape/frame.neon → "frame.neon"
      type = decodeURIComponent(parts[2]);
    } else if (parts[1]) {
      type = decodeURIComponent(parts[1]);
    }
  }

  const params = new URLSearchParams(location.search);
  let props = {};
  if (params.get("props")) {
    try {
      props = JSON.parse(decodeURIComponent(params.get("props")));
    } catch (_) {
      try {
        props = JSON.parse(atob(params.get("props")));
      } catch (__) { /* ignore */ }
    }
  }

  const kind = (type.startsWith("frame.") || type.startsWith("shape.") || type === "shape.vignette" || type === "shape.scene-bg") ? "shape" : "widget";
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
    props: { ...(def.props || {}), ...props }
  };

  const layout = {
    version: 1,
    canvasWidth: item.w,
    canvasHeight: item.h,
    items: [item]
  };

  const runtime = CcsCanvas.createRuntime({
    root: document.getElementById("root"),
    editing: false,
    center: true,
    layout
  });
  runtime.setLayout(layout);

  async function refreshData() {
    try {
      runtime.setData(await CcsCanvas.fetchJson("/data/overlay-data.json"));
    } catch (_) { /* ignore */ }
  }

  CcsCanvas.connectWs((evt) => runtime.handleRealtime(evt));
  refreshData();
  setInterval(refreshData, 1500);

  async function loadChatConfig() {
    try {
      runtime.setChatConfig(await CcsCanvas.fetchJson("/chat/config"));
    } catch (_) { /* ignore */ }
  }

  Promise.all([loadChatConfig(), runtime.loadChatHistory()]);
})();
