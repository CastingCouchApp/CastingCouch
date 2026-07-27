(function () {
  "use strict";

  const params = new URLSearchParams(location.search);
  const instanceId = (location.pathname.split("/").filter(Boolean).pop() || params.get("id") || "").trim();
  function setInstanceLabel(name) {
    const label = document.getElementById("instanceLabel");
    if (!label) return;
    const title = (name || "").trim();
    label.textContent = title
      ? ("Canvas: " + title + " (" + (instanceId || "–") + ")")
      : ("Canvas: " + (instanceId || "–"));
  }

  setInstanceLabel("");

  const stage = document.getElementById("stage");
  const saveStatus = document.getElementById("saveStatus");
  const propsEmpty = document.getElementById("propsEmpty");
  const propsForm = document.getElementById("propsForm");
  const propExtra = document.getElementById("propExtra");

  const runtime = CcsCanvas.createRuntime({
    root: stage,
    editing: true,
    center: true,
    instanceId,
    onSelect: syncProps,
    onChange: scheduleSave
  });

  let ws = null;
  let saveTimer = null;
  let drag = null;
  const SNAP = 8;
  const FALLBACK_PRESETS = [
    { id: "1080p", label: "1920 × 1080 (Full HD)", width: 1920, height: 1080 },
    { id: "720p", label: "1280 × 720 (HD)", width: 1280, height: 720 },
    { id: "1440p", label: "2560 × 1440 (QHD)", width: 2560, height: 1440 },
    { id: "4k", label: "3840 × 2160 (4K)", width: 3840, height: 2160 },
    { id: "1080p-vert", label: "1080 × 1920 (Vertical)", width: 1080, height: 1920 },
    { id: "720p-vert", label: "720 × 1280 (Vertical)", width: 720, height: 1280 },
    { id: "square", label: "1080 × 1080 (Square)", width: 1080, height: 1080 }
  ];
  let sizePresets = FALLBACK_PRESETS.slice();

  const canvasSizePreset = document.getElementById("canvasSizePreset");
  const canvasWidthInput = document.getElementById("canvasWidthInput");
  const canvasHeightInput = document.getElementById("canvasHeightInput");
  const canvasSizeBadge = document.getElementById("canvasSizeBadge");

  function fillSizePresets(list) {
    sizePresets = list && list.length ? list : FALLBACK_PRESETS;
    canvasSizePreset.innerHTML = "";
    for (const p of sizePresets) {
      const opt = document.createElement("option");
      opt.value = p.id;
      opt.textContent = p.label;
      opt.dataset.w = String(p.width);
      opt.dataset.h = String(p.height);
      canvasSizePreset.appendChild(opt);
    }
    const custom = document.createElement("option");
    custom.value = "custom";
    custom.textContent = "Benutzerdefiniert…";
    canvasSizePreset.appendChild(custom);
  }

  function syncCanvasSizeUi() {
    const layout = runtime.getLayout();
    const w = Number(layout.canvasWidth) || 1920;
    const h = Number(layout.canvasHeight) || 1080;
    canvasWidthInput.value = String(w);
    canvasHeightInput.value = String(h);
    canvasSizeBadge.textContent = w + " × " + h;
    const match = sizePresets.find((p) => p.width === w && p.height === h);
    canvasSizePreset.value = match ? match.id : "custom";
  }

  function applyCanvasSize(width, height) {
    const w = Math.round(Number(width));
    const h = Math.round(Number(height));
    if (!(w >= 320 && w <= 7680 && h >= 180 && h <= 4320)) {
      saveStatus.textContent = "Ungültige Größe (320×180 – 7680×4320)";
      return;
    }
    const layout = runtime.getLayout();
    layout.canvasWidth = w;
    layout.canvasHeight = h;
    runtime.setLayout(layout, true);
    syncCanvasSizeUi();
    scheduleSave();
  }

  canvasSizePreset.addEventListener("change", () => {
    if (canvasSizePreset.value === "custom") {
      return;
    }
    const opt = canvasSizePreset.selectedOptions[0];
    if (!opt) return;
    applyCanvasSize(opt.dataset.w, opt.dataset.h);
  });

  document.getElementById("btnApplyCanvasSize").addEventListener("click", () => {
    applyCanvasSize(canvasWidthInput.value, canvasHeightInput.value);
  });

  const widgets = [
    { type: "online", label: "Online + Zeit" },
    { type: "alert", label: "Alert" },
    { type: "music", label: "Music Player" },
    { type: "chat", label: "Chat" },
    { type: "ending-stats", label: "Ending Stats" },
    { type: "text", label: "Text" },
    { type: "image", label: "Image" },
    { type: "countdown", label: "Countdown" },
    { type: "socials", label: "Socials" }
  ];
  const shapes = [
    { type: "frame.rect", label: "Frame Rechteck" },
    { type: "frame.circle", label: "Frame Kreis" },
    { type: "frame.corners", label: "Frame Corners" },
    { type: "frame.bevel", label: "Frame Bezel" },
    { type: "frame.neon", label: "Frame Neon" },
    { type: "frame.dashed", label: "Frame Dashed" },
    { type: "frame.card", label: "Card Frame" },
    { type: "shape.vignette", label: "Vignette" },
    { type: "shape.scene-bg", label: "Starting Hintergrund" }
  ];

  function fillPalette(el, items, kind) {
    el.innerHTML = "";
    for (const entry of items) {
      const btn = document.createElement("div");
      btn.className = "ccs-palette-item";
      btn.textContent = entry.label;
      btn.draggable = true;
      btn.addEventListener("dragstart", (e) => {
        e.dataTransfer.setData("application/ccs-item", JSON.stringify({ type: entry.type, kind }));
      });
      btn.addEventListener("dblclick", () => {
        addItem(entry.type, kind, 120, 120);
      });
      el.appendChild(btn);
    }
  }

  fillPalette(document.getElementById("widgetPalette"), widgets, "widget");
  fillPalette(document.getElementById("shapePalette"), shapes, "shape");

  stage.addEventListener("dragover", (e) => e.preventDefault());
  stage.addEventListener("drop", (e) => {
    e.preventDefault();
    try {
      const payload = JSON.parse(e.dataTransfer.getData("application/ccs-item") || "{}");
      const rect = stage.getBoundingClientRect();
      const layout = runtime.getLayout();
      const scale = Math.min(stage.clientWidth / layout.canvasWidth, stage.clientHeight / layout.canvasHeight);
      const x = snap((e.clientX - rect.left - ((stage.clientWidth - layout.canvasWidth * scale) / 2)) / scale);
      const y = snap((e.clientY - rect.top - ((stage.clientHeight - layout.canvasHeight * scale) / 2)) / scale);
      addItem(payload.type, payload.kind, Math.max(0, x), Math.max(0, y));
    } catch (_) { /* ignore */ }
  });

  function snap(v) {
    return Math.round(v / SNAP) * SNAP;
  }

  function addItem(type, kind, x, y) {
    const layout = runtime.getLayout();
    const item = runtime.createItem(type, kind, x, y);
    layout.items = layout.items || [];
    layout.items.push(item);
    runtime.setLayout(layout, true);
    runtime.select(item.id);
    scheduleSave();
  }

  function selectedItem() {
    const id = runtime.getSelectedId();
    return (runtime.getLayout().items || []).find((i) => i.id === id) || null;
  }

  // After PUT/WS layout echo, setLayout replaces item objects. Prop controls must
  // always mutate the live layout item, never a stale syncProps closure.
  function liveItem(from) {
    const id = (from && from.id) || runtime.getSelectedId();
    if (!id) return null;
    return (runtime.getLayout().items || []).find((i) => i.id === id) || null;
  }

  function commitProp(from, apply) {
    const item = liveItem(from);
    if (!item || item.locked) return null;
    item.props = item.props || {};
    apply(item);
    runtime.renderItems();
    runtime.select(item.id);
    scheduleSave();
    return item;
  }

  function syncProps(item) {
    const btnDelete = document.getElementById("btnDelete");
    if (!item) {
      propsEmpty.hidden = false;
      propsForm.hidden = true;
      btnDelete.disabled = true;
      return;
    }
    propsEmpty.hidden = true;
    propsForm.hidden = false;
    btnDelete.disabled = !!item.locked;
    document.getElementById("propType").value = item.type;
    document.getElementById("propX").value = Math.round(item.x || 0);
    document.getElementById("propY").value = Math.round(item.y || 0);
    document.getElementById("propW").value = Math.round(item.w || 0);
    document.getElementById("propH").value = Math.round(item.h || 0);
    document.getElementById("propZ").value = item.z || 0;
    document.getElementById("propLocked").checked = !!item.locked;
    propExtra.innerHTML = "";
    if (item.type === "online") {
      propExtra.appendChild(boolProp("showClock", "Uhr zeigen", item));
      propExtra.appendChild(boolProp("showUptime", "Uptime zeigen", item));
    } else if (item.type === "alert") {
      propExtra.appendChild(numProp("durationMs", "Dauer (ms)", item, 5000));
    } else if (item.type === "music" || item.type === "spotify") {
      propExtra.appendChild(boolProp("showTitle", "Titel", item));
      propExtra.appendChild(boolProp("showArtist", "Artist", item));
      propExtra.appendChild(boolProp("showAlbumCover", "Cover", item));
      propExtra.appendChild(boolProp("showProgress", "Progress", item));
      propExtra.appendChild(boolProp("hideWhenPaused", "Bei Pause ausblenden", item));
    } else if (item.type === "chat") {
      propExtra.appendChild(boolProp("showTwitchEvents", "Twitch-Events zeigen", item));
      propExtra.appendChild(numProp("maxLines", "Max. Zeilen", item, 80));
      propExtra.appendChild(selectProp("backgroundType", "Hintergrund", item, [
        { value: "None", label: "Keiner (transparent)" },
        { value: "Color", label: "Farbe" },
        { value: "Image", label: "Bild (aus Chat-Einstellungen)" }
      ], "None"));
      propExtra.appendChild(textProp("backgroundColor", "Farbe (#RRGGBB)", item, "#000000"));
      propExtra.appendChild(numProp("backgroundOpacityPercent", "Transparenz %", item, 55));
      propExtra.appendChild(numProp("paddingPx", "Padding px", item, 12));
      propExtra.appendChild(numProp("borderRadiusPx", "Eckenradius px", item, 12));
      propExtra.appendChild(numProp("gapPx", "Abstand px", item, 6));
      propExtra.appendChild(numProp("fontSizePx", "Schriftgröße px", item, 18));
      propExtra.appendChild(textProp("fontFamily", "Schriftart", item, "Segoe UI, system-ui, sans-serif"));
    } else if (item.type === "ending-stats") {
      propExtra.appendChild(selectProp("variant", "Variante", item, [
        { value: "classic", label: "Classic" },
        { value: "neon", label: "Neon" },
        { value: "minimal", label: "Minimal" },
        { value: "cards", label: "Cards" },
        { value: "strip", label: "Strip" },
        { value: "bold", label: "Bold" },
        { value: "outline", label: "Outline" },
        { value: "solid", label: "Solid" },
        { value: "gradient", label: "Gradient" },
        { value: "compact", label: "Compact" }
      ], "classic"));
      propExtra.appendChild(boolProp("showTitle", "Titel zeigen", item));
    } else if (item.type === "text") {
      propExtra.appendChild(textProp("content", "Text", item, "Text"));
      propExtra.appendChild(numProp("fontSizePx", "Schriftgröße px", item, 48));
      propExtra.appendChild(textProp("fontFamily", "Schriftart", item, "Segoe UI, system-ui, sans-serif"));
      propExtra.appendChild(textProp("color", "Farbe", item, "#ffffff"));
      propExtra.appendChild(selectProp("align", "Ausrichtung", item, [
        { value: "left", label: "Links" },
        { value: "center", label: "Mitte" },
        { value: "right", label: "Rechts" }
      ], "center"));
      propExtra.appendChild(selectProp("verticalAlign", "Vertikal", item, [
        { value: "top", label: "Oben" },
        { value: "middle", label: "Mitte" },
        { value: "bottom", label: "Unten" }
      ], "middle"));
      propExtra.appendChild(textProp("fontWeight", "Schriftstärke", item, "700"));
      propExtra.appendChild(numProp("letterSpacingPx", "Zeichenabstand px", item, 0));
      propExtra.appendChild(numProp("lineHeight", "Zeilenhöhe", item, 1.15));
      propExtra.appendChild(textProp("textShadow", "Schatten", item, "0 2px 12px rgba(0,0,0,.55)"));
    } else if (item.type === "image") {
      propExtra.appendChild(textProp("src", "Bild-URL", item, ""));
      propExtra.appendChild(selectProp("fit", "Einpassung", item, [
        { value: "contain", label: "Contain" },
        { value: "cover", label: "Cover" },
        { value: "fill", label: "Fill" },
        { value: "none", label: "None" },
        { value: "scale-down", label: "Scale-down" }
      ], "contain"));
      propExtra.appendChild(numProp("opacity", "Opacity", item, 1));
      propExtra.appendChild(numProp("borderRadiusPx", "Eckenradius px", item, 0));
      propExtra.appendChild(textProp("objectPosition", "Position", item, "center"));
    } else if (item.type === "countdown") {
      propExtra.appendChild(selectProp("variant", "Variante", item, [
        { value: "classic", label: "Classic" },
        { value: "neon", label: "Neon" },
        { value: "minimal", label: "Minimal" },
        { value: "bold", label: "Bold" }
      ], "classic"));
      propExtra.appendChild(selectProp("format", "Format", item, [
        { value: "mm:ss", label: "mm:ss" },
        { value: "hh:mm:ss", label: "hh:mm:ss" },
        { value: "ss", label: "Sekunden" }
      ], "mm:ss"));
      propExtra.appendChild(boolProp("showLabel", "Label zeigen", item));
      propExtra.appendChild(boolProp("hideWhenIdle", "Leer ausblenden", item));
      propExtra.appendChild(numProp("fontSizePx", "Schriftgröße px", item, 72));
      propExtra.appendChild(textProp("color", "Farbe", item, "#ffffff"));
      propExtra.appendChild(selectProp("align", "Ausrichtung", item, [
        { value: "flex-start", label: "Links" },
        { value: "center", label: "Mitte" },
        { value: "flex-end", label: "Rechts" }
      ], "center"));
    } else if (item.type === "socials") {
      propExtra.appendChild(selectProp("variant", "Variante", item, [
        { value: "row", label: "Row" },
        { value: "pills", label: "Pills" },
        { value: "cards", label: "Cards" },
        { value: "stack", label: "Stack" },
        { value: "neon", label: "Neon" },
        { value: "minimal", label: "Minimal" }
      ], "row"));
      propExtra.appendChild(selectProp("iconLibrary", "Icons", item, [
        { value: "svg", label: "Built-in SVG" },
        { value: "fontawesome", label: "Font Awesome" }
      ], "svg"));
      propExtra.appendChild(selectProp("colorMode", "Farben", item, [
        { value: "brand", label: "Brand" },
        { value: "mono", label: "Mono" }
      ], "brand"));
      propExtra.appendChild(boolProp("showLabels", "Labels zeigen", item));
      propExtra.appendChild(boolProp("showHandles", "Handles zeigen", item));
      propExtra.appendChild(numProp("iconSize", "Icon-Größe px", item, 36));
      propExtra.appendChild(numProp("gap", "Abstand px", item, 18));
      propExtra.appendChild(textProp("iconColor", "Mono-Farbe", item, "#ffffff"));

      // showTwitch / twitchHandle / twitchIconUrl …
      propExtra.appendChild(boolProp("showTwitch", "Twitch zeigen", item));
      propExtra.appendChild(textProp("twitchHandle", "Twitch Handle", item, ""));
      propExtra.appendChild(textProp("twitchUrl", "Twitch URL", item, ""));
      propExtra.appendChild(textProp("twitchIconUrl", "Twitch Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showYoutube", "YouTube zeigen", item));
      propExtra.appendChild(textProp("youtubeHandle", "YouTube Handle", item, ""));
      propExtra.appendChild(textProp("youtubeUrl", "YouTube URL", item, ""));
      propExtra.appendChild(textProp("youtubeIconUrl", "YouTube Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showDiscord", "Discord zeigen", item));
      propExtra.appendChild(textProp("discordHandle", "Discord Handle/Invite", item, ""));
      propExtra.appendChild(textProp("discordUrl", "Discord URL", item, ""));
      propExtra.appendChild(textProp("discordIconUrl", "Discord Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showInstagram", "Instagram zeigen", item));
      propExtra.appendChild(textProp("instagramHandle", "Instagram Handle", item, ""));
      propExtra.appendChild(textProp("instagramUrl", "Instagram URL", item, ""));
      propExtra.appendChild(textProp("instagramIconUrl", "Instagram Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showTiktok", "TikTok zeigen", item));
      propExtra.appendChild(textProp("tiktokHandle", "TikTok Handle", item, ""));
      propExtra.appendChild(textProp("tiktokUrl", "TikTok URL", item, ""));
      propExtra.appendChild(textProp("tiktokIconUrl", "TikTok Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showX", "X zeigen", item));
      propExtra.appendChild(textProp("xHandle", "X Handle", item, ""));
      propExtra.appendChild(textProp("xUrl", "X URL", item, ""));
      propExtra.appendChild(textProp("xIconUrl", "X Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showKick", "Kick zeigen", item));
      propExtra.appendChild(textProp("kickHandle", "Kick Handle", item, ""));
      propExtra.appendChild(textProp("kickUrl", "Kick URL", item, ""));
      propExtra.appendChild(textProp("kickIconUrl", "Kick Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showBluesky", "Bluesky zeigen", item));
      propExtra.appendChild(textProp("blueskyHandle", "Bluesky Handle", item, ""));
      propExtra.appendChild(textProp("blueskyUrl", "Bluesky URL", item, ""));
      propExtra.appendChild(textProp("blueskyIconUrl", "Bluesky Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showCustom1", "Custom 1 zeigen", item));
      propExtra.appendChild(textProp("custom1Label", "Custom 1 Label", item, "Custom 1"));
      propExtra.appendChild(textProp("custom1Handle", "Custom 1 Handle", item, ""));
      propExtra.appendChild(textProp("custom1Url", "Custom 1 URL", item, ""));
      propExtra.appendChild(textProp("custom1IconUrl", "Custom 1 Icon-URL", item, ""));

      propExtra.appendChild(boolProp("showCustom2", "Custom 2 zeigen", item));
      propExtra.appendChild(textProp("custom2Label", "Custom 2 Label", item, "Custom 2"));
      propExtra.appendChild(textProp("custom2Handle", "Custom 2 Handle", item, ""));
      propExtra.appendChild(textProp("custom2Url", "Custom 2 URL", item, ""));
      propExtra.appendChild(textProp("custom2IconUrl", "Custom 2 Icon-URL", item, ""));
    } else if (item.type === "frame.card") {
      const sizePresets = CcsCanvas.CARD_FRAME_SIZE_PRESETS || {};
      const sizeOptions = Object.keys(sizePresets).map((key) => ({
        value: key,
        label: sizePresets[key].label || key
      }));
      propExtra.appendChild(selectProp("variant", "Variante", item, [
        { value: "classic", label: "Classic" },
        { value: "neon", label: "Neon" },
        { value: "soft", label: "Soft" },
        { value: "bold", label: "Bold" },
        { value: "outline", label: "Outline" },
        { value: "glass", label: "Glass" },
        { value: "cyber", label: "Cyber" },
        { value: "minimal", label: "Minimal" }
      ], "classic"));
      propExtra.appendChild(selectProp("sizePreset", "Größe", item, sizeOptions, "metaschutz", (live, value) => {
        live.props.sizePreset = value;
        const next = sizePresets[value];
        if (!next) return;
        live.w = next.w;
        live.h = next.h;
      }));
      propExtra.appendChild(textProp("color", "Farbe", item, "#ff7a00"));
      propExtra.appendChild(textProp("color2", "Farbe 2", item, "#ffb36b"));
      propExtra.appendChild(numProp("fillOpacity", "Fill Opacity", item, 0.18));
      propExtra.appendChild(boolProp("showSweep", "Sweep", item));
      propExtra.appendChild(boolProp("showLines", "Linien", item));
    } else if ((item.type || "").startsWith("frame.")) {
      propExtra.appendChild(textProp("color", "Farbe", item, "#ff7a00"));
      if (item.type === "frame.rect") {
        propExtra.appendChild(numProp("radius", "Radius", item, 16));
      }
    } else if (item.type === "shape.scene-bg") {
      const presets = CcsCanvas.SCENE_BG_PRESETS || {};
      const presetKey = String((item.props && item.props.preset) || "ember").toLowerCase();
      const base = presets[presetKey] || presets.ember || {};
      const cfg = Object.assign({}, base, item.props || {});
      const presetOptions = Object.keys(presets).map((key) => ({
        value: key,
        label: presets[key].label || key
      }));
      const presetSelect = selectProp("preset", "Variation", item, presetOptions, "ember", (live, value) => {
          const next = presets[value];
          if (!next) {
            live.props.preset = value;
            return;
          }
          live.props.preset = value;
          ["bgBase", "bgMid", "bgDeep", "glow1", "glow2", "stripeColor", "particleColor",
            "glow1Opacity", "glow2Opacity", "stripeOpacity", "particleOpacity",
            "vignetteOpacity", "scanOpacity"].forEach((key) => {
            if (next[key] != null) live.props[key] = next[key];
          });
          delete live.props.driftDuration;
          delete live.props.particleDuration;
        });
      propExtra.appendChild(presetSelect);
      propExtra.appendChild(textProp("bgBase", "BG Basis", item, cfg.bgBase || "#030303"));
      propExtra.appendChild(textProp("bgMid", "BG Mitte", item, cfg.bgMid || "#101010"));
      propExtra.appendChild(textProp("bgDeep", "BG Tief", item, cfg.bgDeep || "#1a0d03"));
      propExtra.appendChild(textProp("glow1", "Glow 1", item, cfg.glow1 || "#ff7a00"));
      propExtra.appendChild(textProp("glow2", "Glow 2", item, cfg.glow2 || "#ffb36b"));
      propExtra.appendChild(textProp("stripeColor", "Streifen-Farbe", item, cfg.stripeColor || cfg.glow1 || "#ff7a00"));
      propExtra.appendChild(textProp("particleColor", "Partikel-Farbe", item, cfg.particleColor || cfg.glow1 || "#ff7a00"));
      propExtra.appendChild(numProp("glow1Opacity", "Glow-1-Opacity", item, cfg.glow1Opacity != null ? cfg.glow1Opacity : 0.18));
      propExtra.appendChild(numProp("glow2Opacity", "Glow-2-Opacity", item, cfg.glow2Opacity != null ? cfg.glow2Opacity : 0.10));
      propExtra.appendChild(numProp("stripeOpacity", "Streifen-Opacity", item, cfg.stripeOpacity != null ? cfg.stripeOpacity : 0.065));
      propExtra.appendChild(numProp("particleOpacity", "Partikel-Opacity", item, cfg.particleOpacity != null ? cfg.particleOpacity : 0.34));
      propExtra.appendChild(numProp("speed", "Geschwindigkeit", item, cfg.speed != null ? cfg.speed : 1));
      propExtra.appendChild(numProp("driftDuration", "Drift (s)", item, cfg.driftDuration != null ? cfg.driftDuration : 18));
      propExtra.appendChild(numProp("particleDuration", "Partikel (s)", item, cfg.particleDuration != null ? cfg.particleDuration : 22));
      propExtra.appendChild(numProp("vignetteOpacity", "Vignette", item, cfg.vignetteOpacity != null ? cfg.vignetteOpacity : 0));
      propExtra.appendChild(numProp("scanOpacity", "Scanline", item, cfg.scanOpacity != null ? cfg.scanOpacity : 0));
      propExtra.appendChild(boolProp("stripes", "Streifen", item));
      propExtra.appendChild(boolProp("particles", "Partikel", item));
      propExtra.appendChild(boolProp("paused", "Pause", item));
    }
  }

  function boolProp(key, label, item) {
    const wrap = document.createElement("label");
    const checked = item.props && item.props[key] !== false;
    wrap.innerHTML = `<span><input type="checkbox" data-prop="${key}" ${checked ? "checked" : ""}/> ${label}</span>`;
    wrap.querySelector("input").addEventListener("change", (e) => {
      commitProp(item, (live) => {
        live.props[key] = e.target.checked;
      });
    });
    return wrap;
  }

  function numProp(key, label, item, fallback, step) {
    const wrap = document.createElement("label");
    wrap.textContent = label;
    const input = document.createElement("input");
    input.type = "number";
    if (step != null) {
      input.step = String(step);
    } else if (typeof fallback === "number" && !Number.isInteger(fallback)) {
      input.step = "any";
    }
    input.value = item.props && item.props[key] != null ? item.props[key] : fallback;
    input.addEventListener("change", () => {
      commitProp(item, (live) => {
        live.props[key] = Number(input.value);
      });
    });
    wrap.appendChild(input);
    return wrap;
  }

  function textProp(key, label, item, fallback) {
    const wrap = document.createElement("label");
    wrap.textContent = label;
    const input = document.createElement("input");
    input.type = "text";
    input.value = (item.props && item.props[key]) || fallback;
    input.addEventListener("change", () => {
      commitProp(item, (live) => {
        live.props[key] = input.value;
      });
    });
    wrap.appendChild(input);
    return wrap;
  }

  function selectProp(key, label, item, options, fallback, customApply) {
    const wrap = document.createElement("label");
    wrap.textContent = label;
    const select = document.createElement("select");
    const current = item.props && item.props[key] != null ? String(item.props[key]) : String(fallback);
    for (const entry of options) {
      const opt = document.createElement("option");
      opt.value = entry.value;
      opt.textContent = entry.label;
      if (opt.value === current) {
        opt.selected = true;
      }
      select.appendChild(opt);
    }
    select.addEventListener("change", () => {
      commitProp(item, (live) => {
        if (typeof customApply === "function") {
          customApply(live, select.value);
        } else {
          live.props[key] = select.value;
        }
      });
    });
    wrap.appendChild(select);
    return wrap;
  }

  ["propX", "propY", "propW", "propH", "propZ"].forEach((id) => {
    document.getElementById(id).addEventListener("change", () => {
      const item = selectedItem();
      if (!item || item.locked) return;
      item.x = Number(document.getElementById("propX").value) || 0;
      item.y = Number(document.getElementById("propY").value) || 0;
      item.w = Math.max(20, Number(document.getElementById("propW").value) || 20);
      item.h = Math.max(20, Number(document.getElementById("propH").value) || 20);
      item.z = Number(document.getElementById("propZ").value) || 0;
      runtime.renderItems();
      runtime.select(item.id);
      scheduleSave();
    });
  });

  document.getElementById("propLocked").addEventListener("change", (e) => {
    const item = selectedItem();
    if (!item) return;
    item.locked = e.target.checked;
    scheduleSave();
    syncProps(item);
  });

  document.getElementById("btnDelete").addEventListener("click", () => {
    const item = selectedItem();
    if (!item || item.locked) return;
    const layout = runtime.getLayout();
    layout.items = (layout.items || []).filter((i) => i.id !== item.id);
    runtime.setLayout(layout);
    scheduleSave();
  });

  document.getElementById("btnFront").addEventListener("click", () => {
    const item = selectedItem();
    if (!item) return;
    const maxZ = Math.max(0, ...(runtime.getLayout().items || []).map((i) => i.z || 0));
    item.z = maxZ + 1;
    runtime.renderItems();
    runtime.select(item.id);
    scheduleSave();
  });

  document.getElementById("btnBack").addEventListener("click", () => {
    const item = selectedItem();
    if (!item) return;
    item.z = Math.max(0, (item.z || 0) - 1);
    runtime.renderItems();
    runtime.select(item.id);
    scheduleSave();
  });

  stage.addEventListener("pointerdown", (e) => {
    const handle = e.target.closest(".ccs-handle");
    const wrapper = e.target.closest(".ccs-item");
    if (!wrapper) {
      runtime.select(null);
      return;
    }
    const id = wrapper.dataset.id;
    const item = (runtime.getLayout().items || []).find((i) => i.id === id);
    if (!item) return;
    runtime.select(id);
    if (item.locked) return;
    const layout = runtime.getLayout();
    const scale = Math.min(stage.clientWidth / layout.canvasWidth, stage.clientHeight / layout.canvasHeight);
    drag = {
      id,
      mode: handle ? handle.dataset.handle : "move",
      startX: e.clientX,
      startY: e.clientY,
      orig: { x: item.x, y: item.y, w: item.w, h: item.h },
      scale
    };
    stage.setPointerCapture(e.pointerId);
    e.preventDefault();
  });

  stage.addEventListener("pointermove", (e) => {
    if (!drag) return;
    const item = (runtime.getLayout().items || []).find((i) => i.id === drag.id);
    if (!item) return;
    const dx = (e.clientX - drag.startX) / drag.scale;
    const dy = (e.clientY - drag.startY) / drag.scale;
    if (drag.mode === "move") {
      item.x = snap(drag.orig.x + dx);
      item.y = snap(drag.orig.y + dy);
    } else {
      let x = drag.orig.x, y = drag.orig.y, w = drag.orig.w, h = drag.orig.h;
      if (drag.mode.includes("e")) w = Math.max(20, drag.orig.w + dx);
      if (drag.mode.includes("s")) h = Math.max(20, drag.orig.h + dy);
      if (drag.mode.includes("w")) { x = drag.orig.x + dx; w = Math.max(20, drag.orig.w - dx); }
      if (drag.mode.includes("n")) { y = drag.orig.y + dy; h = Math.max(20, drag.orig.h - dy); }
      item.x = snap(x); item.y = snap(y); item.w = snap(w); item.h = snap(h);
    }
    const node = runtime.itemNodes.get(item.id);
    if (node) {
      node.wrapper.style.left = item.x + "px";
      node.wrapper.style.top = item.y + "px";
      node.wrapper.style.width = item.w + "px";
      node.wrapper.style.height = item.h + "px";
    }
    syncProps(item);
  });

  stage.addEventListener("pointerup", () => {
    if (!drag) return;
    drag = null;
    scheduleSave();
  });

  function scheduleSave() {
    saveStatus.textContent = "Speichern…";
    clearTimeout(saveTimer);
    saveTimer = setTimeout(saveNow, 400);
  }

  async function saveNow() {
    if (!instanceId) {
      saveStatus.textContent = "Keine Instanz-ID";
      return;
    }
    const layout = runtime.getLayout();
    try {
      const res = await fetch("/layout/" + encodeURIComponent(instanceId), {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(layout)
      });
      if (!res.ok) throw new Error("HTTP " + res.status);
      if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({
          source: "editor",
          type: "editor.layout.set",
          data: { instanceId, layout: JSON.stringify(layout) }
        }));
      }
      saveStatus.textContent = "Gespeichert " + new Date().toLocaleTimeString();
    } catch (err) {
      saveStatus.textContent = "Fehler: " + (err && err.message ? err.message : err);
    }
  }

  async function boot() {
    fillSizePresets(FALLBACK_PRESETS);
    try {
      fillSizePresets(await CcsCanvas.fetchJson("/canvas/size-presets"));
    } catch (_) { /* fallback already set */ }

    if (!instanceId) {
      saveStatus.textContent = "URL: /editor/{instanceId}";
      syncCanvasSizeUi();
      return;
    }
    try {
      const layout = await CcsCanvas.fetchJson("/layout/" + encodeURIComponent(instanceId));
      runtime.setLayout(layout);
      setInstanceLabel(layout && layout.name);
    } catch (_) {
      runtime.setLayout({ ...CcsCanvas.DEFAULT_LAYOUT, items: [] });
      setInstanceLabel("");
    }
    syncCanvasSizeUi();
    try {
      const data = await CcsCanvas.fetchJson("/data/overlay-data.json");
      runtime.setData(data);
    } catch (_) { /* optional */ }
    try {
      runtime.setChatConfig(await CcsCanvas.fetchJson("/chat/config"));
    } catch (_) { /* optional */ }
    await runtime.loadChatHistory();

    ws = CcsCanvas.connectWs((evt) => {
      runtime.handleRealtime(evt);
      if (evt && evt.type === "app.overlay.layout") {
        syncCanvasSizeUi();
      }
    });
    setInterval(async () => {
      try {
        runtime.setData(await CcsCanvas.fetchJson("/data/overlay-data.json"));
      } catch (_) { /* ignore */ }
    }, 2000);
  }

  boot();
})();
