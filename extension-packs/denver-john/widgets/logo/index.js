(function () {
  if (typeof window === "undefined" || !window.CcsCanvas || typeof window.CcsCanvas.registerWidget !== "function") {
    return;
  }

  var TYPE = "ext:denver-john:logo";
  var DEFAULTS = {
    w: 320,
    h: 110,
    props: {
      monogram: "DJ",
      title: "DENVER JOHN",
      accent: "#ff7a00",
      accent2: "#ffb36b",
      textColor: "#f7f3ee",
      animate: true
    }
  };

  function propsOf(item) {
    var p = (item && item.props) || {};
    return {
      monogram: p.monogram != null && String(p.monogram) !== "" ? String(p.monogram) : DEFAULTS.props.monogram,
      title: p.title != null && String(p.title) !== "" ? String(p.title) : DEFAULTS.props.title,
      accent: p.accent || DEFAULTS.props.accent,
      accent2: p.accent2 || DEFAULTS.props.accent2,
      textColor: p.textColor || DEFAULTS.props.textColor,
      animate: p.animate !== false
    };
  }

  function paint(el, item) {
    var p = propsOf(item);
    el.style.setProperty("--dj-accent", p.accent);
    el.style.setProperty("--dj-accent2", p.accent2);
    el.style.setProperty("--dj-text", p.textColor);
    el.classList.toggle("dj-logo--animate", !!p.animate);

    var mono = el.querySelector("[data-dj-monogram]");
    var title = el.querySelector("[data-dj-title]");
    if (mono) mono.textContent = p.monogram;
    if (title) title.textContent = p.title;
  }

  window.CcsCanvas.registerWidget(TYPE, {
    defaults: DEFAULTS,
    create: function (item) {
      var el = document.createElement("div");
      el.className = "dj-logo";
      el.innerHTML =
        '<svg class="dj-logo__mark" width="118" height="62" viewBox="0 0 118 62" aria-hidden="true">' +
        '<path class="dj-logo__path-d" d="M18 52V10h24c18 0 29 8 29 21S60 52 42 52H18Z"></path>' +
        '<path class="dj-logo__path-j" d="M68 10h28v28c0 11-8 18-20 18-8 0-15-3-19-9"></path>' +
        '<text class="dj-logo__monogram" data-dj-monogram x="34" y="41"></text>' +
        "</svg>" +
        '<div class="dj-logo__title" data-dj-title></div>';
      paint(el, item);
      return el;
    },
    update: function (el, item) {
      paint(el, item);
    }
  });

  try {
    if (window.CcsCanvas.WIDGET_DEFAULTS) {
      window.CcsCanvas.WIDGET_DEFAULTS[TYPE] = {
        w: DEFAULTS.w,
        h: DEFAULTS.h,
        props: Object.assign({}, DEFAULTS.props)
      };
    }
  } catch (_) {
    /* ignore */
  }
})();
