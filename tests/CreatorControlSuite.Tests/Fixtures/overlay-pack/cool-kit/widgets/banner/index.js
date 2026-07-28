(function () {
  if (typeof window === "undefined" || !window.CcsCanvas || typeof window.CcsCanvas.registerWidget !== "function") {
    return;
  }

  window.CcsCanvas.registerWidget("ext:cool-kit:banner", {
    defaults: { w: 400, h: 120, props: { text: "Cool Banner" } },
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
