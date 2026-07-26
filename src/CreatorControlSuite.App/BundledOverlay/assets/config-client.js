(() => {
  "use strict";
  const listeners = new Set();
  let timer = null;
  async function load() {
    try {
      const r = await fetch("../data/overlay-config.json?ts=" + Date.now(), { cache: "no-store" });
      if (!r.ok) return;
      const cfg = await r.json();
      document.documentElement.style.setProperty("--user-font", cfg.fontFamily || "Segoe UI");
      document.documentElement.style.setProperty("--user-color", cfg.fontColor || "#ffffff");
      listeners.forEach(x => x(cfg));
    } catch {}
  }
  window.CreatorOverlayConfig = { subscribe(listener) { listeners.add(listener); load(); if (!timer) timer=setInterval(load,1000); return ()=>listeners.delete(listener); } };
})();
