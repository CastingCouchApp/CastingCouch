import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "./context";
import { boolProp } from "../controls/bool-prop";
import { numProp } from "../controls/num-prop";
import { textProp } from "../controls/text-prop";
import { selectProp } from "../controls/select-prop";
import { fontProp } from "../controls/font-prop";
import { colorProp } from "../controls/color-prop";
import { propSection } from "../sections/prop-section";
import { renderEffectsPanel } from "../effects/effects-panel";

export function syncProps(
  item: LayoutItem | null,
  ctx: EditorContext,
  propExtra: HTMLElement,
  propsEmpty: HTMLElement,
  propsForm: HTMLElement,
  btnDelete: HTMLButtonElement
): void {
  if (!item) {
    propsEmpty.hidden = false;
    propsForm.hidden = true;
    btnDelete.disabled = true;
    return;
  }
  propsEmpty.hidden = true;
  propsForm.hidden = false;
  btnDelete.disabled = !!item.locked;
  (document.getElementById("propType") as HTMLInputElement).value = item.type;
  (document.getElementById("propX") as HTMLInputElement).value = String(Math.round(item.x || 0));
  (document.getElementById("propY") as HTMLInputElement).value = String(Math.round(item.y || 0));
  (document.getElementById("propW") as HTMLInputElement).value = String(Math.round(item.w || 0));
  (document.getElementById("propH") as HTMLInputElement).value = String(Math.round(item.h || 0));
  (document.getElementById("propZ") as HTMLInputElement).value = String(item.z || 0);
  (document.getElementById("propLocked") as HTMLInputElement).checked = !!item.locked;
  propExtra.innerHTML = "";

  if (item.type === "online") {
    propExtra.appendChild(boolProp("showClock", "Uhr zeigen", item, ctx));
    propExtra.appendChild(boolProp("showUptime", "Uptime zeigen", item, ctx));
  } else if (item.type === "alert") {
    propExtra.appendChild(numProp("durationMs", "Dauer (ms)", item, ctx, 5000));
  } else if (item.type === "music" || item.type === "spotify") {
    appendMusicProps(item, ctx, propExtra);
  } else if (item.type === "chat") {
    propExtra.appendChild(boolProp("showTwitchEvents", "Twitch-Events zeigen", item, ctx));
    propExtra.appendChild(numProp("maxLines", "Max. Zeilen", item, ctx, 80));
    propExtra.appendChild(selectProp("backgroundType", "Hintergrund", item, ctx, [
      { value: "None", label: "Keiner (transparent)" },
      { value: "Color", label: "Farbe" },
      { value: "Image", label: "Bild (aus Chat-Einstellungen)" }
    ], "None"));
    propExtra.appendChild(colorProp("backgroundColor", "Farbe", item, ctx, "#000000"));
    propExtra.appendChild(numProp("backgroundOpacityPercent", "Transparenz %", item, ctx, 55));
    propExtra.appendChild(numProp("paddingPx", "Padding px", item, ctx, 12));
    propExtra.appendChild(numProp("borderRadiusPx", "Eckenradius px", item, ctx, 12));
    propExtra.appendChild(numProp("gapPx", "Abstand px", item, ctx, 6));
    propExtra.appendChild(numProp("fontSizePx", "Schriftgröße px", item, ctx, 18));
    propExtra.appendChild(fontProp("fontFamily", "Schriftart", item, ctx, "Segoe UI, system-ui, sans-serif"));
  } else if (item.type === "ending-stats") {
    propExtra.appendChild(selectProp("variant", "Variante", item, ctx, [
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
    propExtra.appendChild(boolProp("showTitle", "Titel zeigen", item, ctx));
  } else if (item.type === "text") {
    propExtra.appendChild(textProp("content", "Text", item, ctx, "Text"));
    propExtra.appendChild(numProp("fontSizePx", "Schriftgröße px", item, ctx, 48));
    propExtra.appendChild(fontProp("fontFamily", "Schriftart", item, ctx, "Segoe UI, system-ui, sans-serif"));
    propExtra.appendChild(colorProp("color", "Farbe", item, ctx, "#ffffff"));
    propExtra.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
      { value: "left", label: "Links" },
      { value: "center", label: "Mitte" },
      { value: "right", label: "Rechts" }
    ], "center"));
    propExtra.appendChild(selectProp("verticalAlign", "Vertikal", item, ctx, [
      { value: "top", label: "Oben" },
      { value: "middle", label: "Mitte" },
      { value: "bottom", label: "Unten" }
    ], "middle"));
    propExtra.appendChild(textProp("fontWeight", "Schriftstärke", item, ctx, "700"));
    propExtra.appendChild(numProp("letterSpacingPx", "Zeichenabstand px", item, ctx, 0));
    propExtra.appendChild(numProp("lineHeight", "Zeilenhöhe", item, ctx, 1.15));
    propExtra.appendChild(textProp("textShadow", "Schatten", item, ctx, "0 2px 12px rgba(0,0,0,.55)"));
  } else if (item.type === "image") {
    propExtra.appendChild(textProp("src", "Bild-URL", item, ctx, ""));
    propExtra.appendChild(selectProp("fit", "Einpassung", item, ctx, [
      { value: "contain", label: "Contain" },
      { value: "cover", label: "Cover" },
      { value: "fill", label: "Fill" },
      { value: "none", label: "None" },
      { value: "scale-down", label: "Scale-down" }
    ], "contain"));
    propExtra.appendChild(numProp("opacity", "Opacity", item, ctx, 1));
    propExtra.appendChild(numProp("borderRadiusPx", "Eckenradius px", item, ctx, 0));
    propExtra.appendChild(textProp("objectPosition", "Position", item, ctx, "center"));
  } else if (item.type === "countdown") {
    propExtra.appendChild(selectProp("variant", "Variante", item, ctx, [
      { value: "classic", label: "Classic" },
      { value: "neon", label: "Neon" },
      { value: "minimal", label: "Minimal" },
      { value: "bold", label: "Bold" }
    ], "classic"));
    propExtra.appendChild(selectProp("format", "Format", item, ctx, [
      { value: "mm:ss", label: "mm:ss" },
      { value: "hh:mm:ss", label: "hh:mm:ss" },
      { value: "ss", label: "Sekunden" }
    ], "mm:ss"));
    propExtra.appendChild(boolProp("showLabel", "Label zeigen", item, ctx));
    propExtra.appendChild(boolProp("hideWhenIdle", "Leer ausblenden", item, ctx));
    propExtra.appendChild(numProp("fontSizePx", "Schriftgröße px", item, ctx, 72));
    propExtra.appendChild(colorProp("color", "Farbe", item, ctx, "#ffffff"));
    propExtra.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
      { value: "flex-start", label: "Links" },
      { value: "center", label: "Mitte" },
      { value: "flex-end", label: "Rechts" }
    ], "center"));
  } else if (item.type === "socials") {
    appendSocialsProps(item, ctx, propExtra);
  } else if (item.type === "partner-roulette") {
    appendPartnerRouletteProps(item, ctx, propExtra);
  } else if (item.type === "frame.card") {
    appendFrameCardProps(item, ctx, propExtra);
  } else if (item.type === "frame" || (item.type || "").startsWith("frame.")) {
    appendFrameProps(item, ctx, propExtra);
  } else if (item.type === "shape.cutout") {
    propExtra.appendChild(numProp("radius", "Eckenradius px", item, ctx, 24));
  } else if (item.type === "shape.scene-bg") {
    appendSceneBgProps(item, ctx, propExtra);
  }

  renderEffectsPanel(propExtra, item, ctx);
}

function appendMusicProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = window.CcsCanvas.MUSIC_VARIANTS || [];
  const labels = window.CcsCanvas.MUSIC_VARIANT_LABELS || {};
  const sizePresets = window.CcsCanvas.MUSIC_SIZE_PRESETS || {};

  const look = propSection("music-look", "Look & Größe");
  look.body.appendChild(selectProp("variant", "Style", item, ctx, variants.map((key) => ({
    value: key,
    label: (labels as Record<string, string>)[key] || key
  })), "classic"));
  look.body.appendChild(selectProp("sizePreset", "Größe", item, ctx, Object.keys(sizePresets).map((key) => ({
    value: key,
    label: (sizePresets as Record<string, { label?: string }>)[key].label || key
  })), "standard", (live, value) => {
    live.props.sizePreset = value;
    const next = (sizePresets as Record<string, { w: number; h: number }>)[value];
    if (!next) return;
    live.w = next.w;
    live.h = next.h;
  }));
  propExtra.appendChild(look.root);

  const content = propSection("music-content", "Inhalt");
  content.body.appendChild(boolProp("showTitle", "Titel", item, ctx));
  content.body.appendChild(boolProp("showArtist", "Artist", item, ctx));
  content.body.appendChild(boolProp("showAlbumCover", "Cover", item, ctx));
  content.body.appendChild(boolProp("showProgress", "Progress", item, ctx));
  content.body.appendChild(boolProp("hideWhenPaused", "Bei Pause ausblenden", item, ctx));
  propExtra.appendChild(content.root);
}

function appendSocialsProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  propExtra.appendChild(selectProp("platform", "Plattform", item, ctx, [
    { value: "twitch", label: "Twitch" },
    { value: "youtube", label: "YouTube" },
    { value: "discord", label: "Discord" },
    { value: "instagram", label: "Instagram" },
    { value: "tiktok", label: "TikTok" },
    { value: "x", label: "X" },
    { value: "kick", label: "Kick" },
    { value: "bluesky", label: "Bluesky" },
    { value: "custom1", label: "Custom 1" },
    { value: "custom2", label: "Custom 2" }
  ], "twitch"));
  propExtra.appendChild(textProp("handle", "Handle", item, ctx, ""));
  propExtra.appendChild(textProp("url", "URL (optional)", item, ctx, ""));
  propExtra.appendChild(textProp("label", "Label (optional)", item, ctx, ""));
  propExtra.appendChild(textProp("iconUrl", "Icon-URL (optional)", item, ctx, ""));
  propExtra.appendChild(selectProp("variant", "Variante", item, ctx, [
    { value: "row", label: "Row" },
    { value: "pills", label: "Pills" },
    { value: "cards", label: "Cards" },
    { value: "stack", label: "Stack" },
    { value: "neon", label: "Neon" },
    { value: "minimal", label: "Minimal" }
  ], "pills"));
  propExtra.appendChild(selectProp("iconLibrary", "Icons", item, ctx, [
    { value: "svg", label: "Built-in SVG" },
    { value: "fontawesome", label: "Font Awesome" }
  ], "svg"));
  propExtra.appendChild(selectProp("colorMode", "Farben", item, ctx, [
    { value: "brand", label: "Brand" },
    { value: "mono", label: "Mono" }
  ], "brand"));
  propExtra.appendChild(boolProp("showLabels", "Labels zeigen", item, ctx));
  propExtra.appendChild(boolProp("showHandles", "Handles zeigen", item, ctx));
  propExtra.appendChild(numProp("iconSize", "Icon-Größe px", item, ctx, 36));
  propExtra.appendChild(numProp("gap", "Abstand px", item, ctx, 12));
  propExtra.appendChild(colorProp("iconColor", "Mono-Farbe", item, ctx, "#ffffff"));
}

function readRouletteImages(item: LayoutItem): string[] {
  const live = item.props?.images;
  if (!Array.isArray(live)) return [];
  return live.map((entry) => {
    if (typeof entry === "string") return entry;
    if (entry && typeof entry === "object") return String((entry as { src?: unknown }).src || "");
    return "";
  });
}

function appendPartnerRouletteProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const timing = propSection("partner-roulette-timing", "Timing & Übergang");
  timing.body.appendChild(numProp("intervalMs", "Anzeige (ms)", item, ctx, 4000, {
    min: 500,
    max: 60000,
    step: 100
  }));
  timing.body.appendChild(selectProp("transition", "Übergang", item, ctx, [
    { value: "fade", label: "Fade" },
    { value: "crossfade", label: "Crossfade" },
    { value: "slide", label: "Slide" },
    { value: "none", label: "Keiner" }
  ], "fade"));
  timing.body.appendChild(numProp("transitionMs", "Übergang (ms)", item, ctx, 500, {
    min: 0,
    max: 3000,
    step: 50
  }));
  propExtra.appendChild(timing.root);

  const look = propSection("partner-roulette-look", "Darstellung");
  look.body.appendChild(selectProp("fit", "Einpassung", item, ctx, [
    { value: "contain", label: "Contain" },
    { value: "cover", label: "Cover" },
    { value: "fill", label: "Fill" },
    { value: "none", label: "None" },
    { value: "scale-down", label: "Scale-down" }
  ], "contain"));
  look.body.appendChild(numProp("borderRadiusPx", "Eckenradius px", item, ctx, 12));
  look.body.appendChild(textProp("objectPosition", "Position", item, ctx, "center"));
  propExtra.appendChild(look.root);

  const imagesSection = propSection("partner-roulette-images", "Bilder");
  const list = document.createElement("div");
  list.className = "ccs-effects-list";

  function renderList(): void {
    list.innerHTML = "";
    const live = ctx.liveItem(item) || item;
    const images = readRouletteImages(live);
    images.forEach((src, i) => {
      const card = document.createElement("div");
      card.className = "ccs-effect-instance";

      const row = document.createElement("div");
      row.className = "ccs-effect-row";

      const input = document.createElement("input");
      input.type = "text";
      input.value = src;
      input.placeholder = "https://… /media/…";
      input.style.flex = "1";
      input.addEventListener("change", () => {
        ctx.commitProp(item, (next) => {
          const nextImages = readRouletteImages(next);
          nextImages[i] = input.value;
          next.props.images = nextImages;
        });
      });
      input.addEventListener("input", () => {
        if (!ctx.previewProp) return;
        ctx.previewProp(item, (next) => {
          const nextImages = readRouletteImages(next);
          nextImages[i] = input.value;
          next.props.images = nextImages;
        });
      });

      const remove = document.createElement("button");
      remove.type = "button";
      remove.textContent = "×";
      remove.title = "Entfernen";
      remove.addEventListener("click", () => {
        ctx.commitProp(item, (next) => {
          const nextImages = readRouletteImages(next);
          nextImages.splice(i, 1);
          next.props.images = nextImages;
        });
        renderList();
      });

      row.appendChild(input);
      row.appendChild(remove);
      card.appendChild(row);
      list.appendChild(card);
    });
  }

  const addBtn = document.createElement("button");
  addBtn.type = "button";
  addBtn.textContent = "Bild hinzufügen";
  addBtn.className = "ccs-palette-item";
  addBtn.addEventListener("click", () => {
    ctx.commitProp(item, (live) => {
      const nextImages = readRouletteImages(live);
      nextImages.push("");
      live.props.images = nextImages;
    });
    renderList();
  });

  renderList();
  imagesSection.body.appendChild(list);
  imagesSection.body.appendChild(addBtn);
  propExtra.appendChild(imagesSection.root);
}

function appendFrameProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const modes = window.CcsCanvas.FRAME_MODES || [];
  const labels = window.CcsCanvas.FRAME_MODE_LABELS || {};
  const modeOptions = modes.map((key) => ({
    value: key,
    label: (labels as Record<string, string>)[key] || key
  }));
  const legacyFallback: Record<string, string> = {
    "frame.rect": "rect",
    "frame.circle": "circle",
    "frame.corners": "corners",
    "frame.bevel": "bevel",
    "frame.neon": "neon",
    "frame.dashed": "dashed"
  };
  const modeFallback = legacyFallback[item.type] || "rect";
  propExtra.appendChild(selectProp("mode", "Modus", item, ctx, modeOptions, modeFallback, (live, value) => {
    live.props.mode = value;
    // Normalize legacy shape ids to the unified frame type once the user picks a mode.
    if (live.type !== "frame") live.type = "frame";
  }));
  propExtra.appendChild(colorProp("color", "Farbe", item, ctx, "#ff7a00"));
  propExtra.appendChild(numProp("radius", "Eckenradius px", item, ctx, 16));
}

function appendFrameCardProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const sizePresets = window.CcsCanvas.CARD_FRAME_SIZE_PRESETS || {};
  const sizeOptions = Object.keys(sizePresets).map((key) => ({
    value: key,
    label: (sizePresets as Record<string, { label?: string }>)[key].label || key
  }));
  propExtra.appendChild(selectProp("variant", "Variante", item, ctx, [
    { value: "classic", label: "Classic" },
    { value: "neon", label: "Neon" },
    { value: "soft", label: "Soft" },
    { value: "bold", label: "Bold" },
    { value: "outline", label: "Outline" },
    { value: "glass", label: "Glass" },
    { value: "cyber", label: "Cyber" },
    { value: "minimal", label: "Minimal" }
  ], "classic"));
  propExtra.appendChild(selectProp("sizePreset", "Größe", item, ctx, sizeOptions, "metaschutz", (live, value) => {
    live.props.sizePreset = value;
    const next = (sizePresets as Record<string, { w: number; h: number }>)[value];
    if (!next) return;
    live.w = next.w;
    live.h = next.h;
  }));
  propExtra.appendChild(colorProp("color", "Farbe", item, ctx, "#ff7a00"));
  propExtra.appendChild(colorProp("color2", "Farbe 2", item, ctx, "#ffb36b"));
  propExtra.appendChild(numProp("fillOpacity", "Fill Opacity", item, ctx, 0.18));
  propExtra.appendChild(boolProp("showSweep", "Sweep", item, ctx));
  propExtra.appendChild(boolProp("showLines", "Linien", item, ctx));
}

function appendSceneBgProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const presets = window.CcsCanvas.SCENE_BG_PRESETS || {};
  const presetKey = String((item.props && item.props.preset) || "ember").toLowerCase();
  const base = (presets as Record<string, Record<string, unknown>>)[presetKey] || (presets as Record<string, Record<string, unknown>>).ember || {};
  const cfg = Object.assign({}, base, item.props || {});
  const presetOptions = Object.keys(presets).map((key) => ({
    value: key,
    label: String((presets as Record<string, { label?: string }>)[key].label || key)
  }));
  propExtra.appendChild(selectProp("preset", "Variation", item, ctx, presetOptions, "ember", (live, value) => {
    const next = (presets as Record<string, Record<string, unknown>>)[value];
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
  }));
  propExtra.appendChild(colorProp("bgBase", "BG Basis", item, ctx, String(cfg.bgBase || "#030303")));
  propExtra.appendChild(colorProp("bgMid", "BG Mitte", item, ctx, String(cfg.bgMid || "#101010")));
  propExtra.appendChild(colorProp("bgDeep", "BG Tief", item, ctx, String(cfg.bgDeep || "#1a0d03")));
  propExtra.appendChild(colorProp("glow1", "Glow 1", item, ctx, String(cfg.glow1 || "#ff7a00")));
  propExtra.appendChild(colorProp("glow2", "Glow 2", item, ctx, String(cfg.glow2 || "#ffb36b")));
  propExtra.appendChild(colorProp("stripeColor", "Streifen-Farbe", item, ctx, String(cfg.stripeColor || cfg.glow1 || "#ff7a00")));
  propExtra.appendChild(colorProp("particleColor", "Partikel-Farbe", item, ctx, String(cfg.particleColor || cfg.glow1 || "#ff7a00")));
  propExtra.appendChild(numProp("glow1Opacity", "Glow-1-Opacity", item, ctx, Number(cfg.glow1Opacity ?? 0.18)));
  propExtra.appendChild(numProp("glow2Opacity", "Glow-2-Opacity", item, ctx, Number(cfg.glow2Opacity ?? 0.10)));
  propExtra.appendChild(numProp("stripeOpacity", "Streifen-Opacity", item, ctx, Number(cfg.stripeOpacity ?? 0.065)));
  propExtra.appendChild(numProp("particleOpacity", "Partikel-Opacity", item, ctx, Number(cfg.particleOpacity ?? 0.34)));
  propExtra.appendChild(numProp("speed", "Geschwindigkeit", item, ctx, Number(cfg.speed ?? 1)));
  propExtra.appendChild(numProp("driftDuration", "Drift (s)", item, ctx, Number(cfg.driftDuration ?? 18)));
  propExtra.appendChild(numProp("particleDuration", "Partikel (s)", item, ctx, Number(cfg.particleDuration ?? 22)));
  propExtra.appendChild(numProp("vignetteOpacity", "Vignette", item, ctx, Number(cfg.vignetteOpacity ?? 0)));
  propExtra.appendChild(numProp("scanOpacity", "Scanline", item, ctx, Number(cfg.scanOpacity ?? 0)));
  propExtra.appendChild(boolProp("stripes", "Streifen", item, ctx));
  propExtra.appendChild(boolProp("particles", "Partikel", item, ctx));
  propExtra.appendChild(boolProp("paused", "Pause", item, ctx));
}
