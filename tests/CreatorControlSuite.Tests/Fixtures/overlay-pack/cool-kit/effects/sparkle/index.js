(function () {
  if (typeof window === "undefined" || !window.CcsCanvas || typeof window.CcsCanvas.registerEffect !== "function") {
    return;
  }

  window.CcsCanvas.registerEffect("cool-kit/sparkle", {
    apply(el) {
      el.classList.add("cool-kit-sparkle");
    }
  });
})();
