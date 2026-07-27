(function () {
  if (typeof window === "undefined" || !window.CcsCanvas || typeof window.CcsCanvas.registerWidget !== "function") {
    return;
  }

  window.CcsCanvas.registerWidget("cool-kit/banner", {
    create() {
      var el = document.createElement("div");
      el.className = "cool-kit-banner";
      return el;
    },
    update(el, item) {
      el.textContent = (item && item.props && item.props.text) || "Cool Banner";
    }
  });
})();
