(function () {
  if (typeof window === "undefined" || !window.CcsCanvas || typeof window.CcsCanvas.registerAnimation !== "function") {
    return;
  }

  window.CcsCanvas.registerAnimation("ext:cool-kit:wobble", {
    label: "Wobble",
    defaults: { intensity: 0.6, durationMs: 900 },
    fields: [
      { key: "intensity", kind: "number", label: "Intensität", min: 0, max: 1, step: 0.05, fallback: 0.6 },
      { key: "durationMs", kind: "number", label: "Dauer (ms)", min: 200, max: 4000, step: 50, fallback: 900 }
    ],
    apply(el, animation) {
      if (!el || !el.style) return;
      var settings = (animation && animation.settings) || {};
      var intensity = Number(settings.intensity);
      if (!Number.isFinite(intensity)) intensity = 0.6;
      var duration = Number(settings.durationMs);
      if (!Number.isFinite(duration)) duration = 900;
      el.style.setProperty("--cool-kit-wobble-i", String(intensity));
      el.style.animation = "cool-kit-wobble " + duration + "ms ease-in-out infinite";
    }
  });
})();
