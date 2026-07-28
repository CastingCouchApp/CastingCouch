(function () {
  if (typeof window === "undefined" || !window.CcsCanvas || typeof window.CcsCanvas.registerEffect !== "function") {
    return;
  }

  window.CcsCanvas.registerEffect("ext:cool-kit:sparkle", {
    label: "Sparkle",
    defaults: { intensity: 0.5 },
    fields: [{ key: "intensity", kind: "number", label: "Intensität", min: 0, max: 1, step: 0.05, fallback: 0.5 }],
    apply(layer) {
      if (layer && layer.classList) {
        layer.classList.add("cool-kit-sparkle");
      }
    }
  });
})();
