(() => {
  "use strict";

  const listeners = new Set();
  let timer = null;
  let lastVersion = "";

  async function load() {
    try {
      const response = await fetch(
        "../data/overlay-data.json?ts=" + Date.now(),
        { cache: "no-store" }
      );

      if (!response.ok) return;

      const data = await response.json();
      const version = String(data.updatedAt || "");

      if (version !== lastVersion) {
        lastVersion = version;
        listeners.forEach(listener => listener(data));
      }
    } catch {
    }
  }

  window.CreatorOverlayData = {
    subscribe(listener) {
      listeners.add(listener);
      load();

      if (!timer) {
        timer = setInterval(load, 500);
      }

      return () => listeners.delete(listener);
    }
  };
})();
