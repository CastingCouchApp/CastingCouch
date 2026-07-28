import type { LayoutItem } from "../../shared/types";
import type { EditorContext } from "./context";
import { boolProp } from "../controls/bool-prop";
import { numProp } from "../controls/num-prop";
import { textProp } from "../controls/text-prop";
import { imageProp, attachImageLibraryButton } from "../controls/image-prop";
import { selectProp } from "../controls/select-prop";
import { fontProp } from "../controls/font-prop";
import { colorProp } from "../controls/color-prop";
import {
  advancedSection,
  contentSection,
  lookSection,
  propSection,
  styleSection
} from "../sections/prop-section";
import { renderEffectsPanel } from "../effects/effects-panel";
import { renderAnimationsPanel } from "../animations/animations-panel";
import {
  appendGoalBarProps,
  appendEventTickerProps,
  appendViewerCountProps,
  appendLowerThirdProps,
  appendQrCodeProps,
  appendBrbPanelProps,
  appendAnnouncementBarProps,
  appendBubatzCantinaProps,
  appendFruppisLandadelProps,
  appendAnimatedBackgroundProps,
  appendDividerProps,
  appendCamRingProps,
  appendStickerProps,
  appendChatProps
} from "./panels/new-streamer-widgets";

export function syncProps(
  item: LayoutItem | null,
  ctx: EditorContext,
  propExtra: HTMLElement,
  propsEmpty: HTMLElement,
  propsForm: HTMLElement,
  btnDelete: HTMLButtonElement,
  propEffects?: HTMLElement | null,
  propAnimations?: HTMLElement | null
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
  (document.getElementById("propPadding") as HTMLInputElement).value = String(
    Math.max(0, Math.round(Number(item.padding) || 0))
  );
  (document.getElementById("propLocked") as HTMLInputElement).checked = !!item.locked;
  propExtra.innerHTML = "";
  const effectsPane = propEffects || document.getElementById("propEffects");
  const animationsPane = propAnimations || document.getElementById("propAnimations");
  if (effectsPane) effectsPane.innerHTML = "";
  if (animationsPane) animationsPane.innerHTML = "";

  if (item.type === "online") {
    const content = contentSection("online");
    content.body.appendChild(boolProp("showClock", "Uhr zeigen", item, ctx));
    content.body.appendChild(boolProp("showUptime", "Uptime zeigen", item, ctx));
    propExtra.appendChild(content.root);
  } else if (item.type === "alert") {
    const content = contentSection("alert");
    content.body.appendChild(numProp("durationMs", "Dauer (ms)", item, ctx, 5000));
    propExtra.appendChild(content.root);
  } else if (item.type === "music" || item.type === "spotify") {
    appendMusicProps(item, ctx, propExtra);
  } else if (item.type === "chat") {
    appendChatProps(item, ctx, propExtra);
  } else if (item.type === "ending-stats") {
    const content = contentSection("ending-stats");
    content.body.appendChild(selectProp("variant", "Variante", item, ctx, [
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
    content.body.appendChild(boolProp("showTitle", "Titel zeigen", item, ctx));
    propExtra.appendChild(content.root);
  } else if (item.type === "text") {
    const content = contentSection("text");
    content.body.appendChild(textProp("content", "Text", item, ctx, "Text"));
    content.body.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
      { value: "left", label: "Links" },
      { value: "center", label: "Mitte" },
      { value: "right", label: "Rechts" }
    ], "center"));
    content.body.appendChild(selectProp("verticalAlign", "Vertikal", item, ctx, [
      { value: "top", label: "Oben" },
      { value: "middle", label: "Mitte" },
      { value: "bottom", label: "Unten" }
    ], "middle"));
    propExtra.appendChild(content.root);

    const style = styleSection("text");
    style.body.appendChild(numProp("fontSizePx", "Schriftgröße px", item, ctx, 48));
    style.body.appendChild(fontProp("fontFamily", "Schriftart", item, ctx, "Segoe UI, system-ui, sans-serif"));
    style.body.appendChild(colorProp("color", "Farbe", item, ctx, "#ffffff"));
    style.body.appendChild(textProp("fontWeight", "Schriftstärke", item, ctx, "700"));
    style.body.appendChild(numProp("letterSpacingPx", "Zeichenabstand px", item, ctx, 0));
    style.body.appendChild(numProp("lineHeight", "Zeilenhöhe", item, ctx, 1.15));
    style.body.appendChild(textProp("textShadow", "Schatten", item, ctx, "0 2px 12px rgba(0,0,0,.55)"));
    propExtra.appendChild(style.root);
  } else if (item.type === "image") {
    const content = contentSection("image");
    content.body.appendChild(imageProp("src", "Bild-URL", item, ctx, ""));
    content.body.appendChild(selectProp("fit", "Einpassung", item, ctx, [
      { value: "contain", label: "Contain" },
      { value: "cover", label: "Cover" },
      { value: "fill", label: "Fill" },
      { value: "none", label: "None" },
      { value: "scale-down", label: "Scale-down" }
    ], "contain"));
    propExtra.appendChild(content.root);

    const style = styleSection("image");
    style.body.appendChild(numProp("opacity", "Opacity", item, ctx, 1));
    style.body.appendChild(numProp("borderRadiusPx", "Eckenradius px", item, ctx, 0));
    style.body.appendChild(textProp("objectPosition", "Position", item, ctx, "center"));
    propExtra.appendChild(style.root);
  } else if (item.type === "countdown") {
    const content = contentSection("countdown");
    content.body.appendChild(selectProp("variant", "Variante", item, ctx, [
      { value: "classic", label: "Classic" },
      { value: "neon", label: "Neon" },
      { value: "minimal", label: "Minimal" },
      { value: "bold", label: "Bold" }
    ], "classic"));
    content.body.appendChild(selectProp("format", "Format", item, ctx, [
      { value: "mm:ss", label: "mm:ss" },
      { value: "hh:mm:ss", label: "hh:mm:ss" },
      { value: "ss", label: "Sekunden" }
    ], "mm:ss"));
    content.body.appendChild(boolProp("showLabel", "Label zeigen", item, ctx));
    content.body.appendChild(boolProp("hideWhenIdle", "Leer ausblenden", item, ctx));
    content.body.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
      { value: "flex-start", label: "Links" },
      { value: "center", label: "Mitte" },
      { value: "flex-end", label: "Rechts" }
    ], "center"));
    propExtra.appendChild(content.root);

    const style = styleSection("countdown");
    style.body.appendChild(numProp("fontSizePx", "Schriftgröße px", item, ctx, 72));
    style.body.appendChild(colorProp("color", "Farbe", item, ctx, "#ffffff"));
    propExtra.appendChild(style.root);
  } else if (item.type === "socials") {
    appendSocialsProps(item, ctx, propExtra);
  } else if (item.type === "partner-roulette") {
    appendPartnerRouletteProps(item, ctx, propExtra);
  } else if (item.type === "goal-bar") {
    appendGoalBarProps(item, ctx, propExtra);
  } else if (item.type === "event-ticker") {
    appendEventTickerProps(item, ctx, propExtra);
  } else if (item.type === "viewer-count") {
    appendViewerCountProps(item, ctx, propExtra);
  } else if (item.type === "lower-third") {
    appendLowerThirdProps(item, ctx, propExtra);
  } else if (item.type === "qr-code") {
    appendQrCodeProps(item, ctx, propExtra);
  } else if (item.type === "brb-panel") {
    appendBrbPanelProps(item, ctx, propExtra);
  } else if (item.type === "announcement-bar") {
    appendAnnouncementBarProps(item, ctx, propExtra);
  } else if (item.type === "bubatz-cantina") {
    appendBubatzCantinaProps(item, ctx, propExtra);
  } else if (item.type === "fruppis-landadel") {
    appendFruppisLandadelProps(item, ctx, propExtra);
  } else if (item.type === "animated-background") {
    appendAnimatedBackgroundProps(item, ctx, propExtra);
  } else if (item.type === "frame.card") {
    appendFrameCardProps(item, ctx, propExtra);
  } else if (item.type === "frame" || (item.type || "").startsWith("frame.")) {
    appendFrameProps(item, ctx, propExtra);
  } else if (item.type === "shape.cutout") {
    const style = styleSection("shape-cutout");
    style.body.appendChild(numProp("radius", "Eckenradius px", item, ctx, 24));
    propExtra.appendChild(style.root);
  } else if (item.type === "shape.divider") {
    appendDividerProps(item, ctx, propExtra);
  } else if (item.type === "shape.cam-ring") {
    appendCamRingProps(item, ctx, propExtra);
  } else if (item.type === "shape.sticker") {
    appendStickerProps(item, ctx, propExtra);
  } else if (item.type === "shape.scene-bg") {
    appendSceneBgProps(item, ctx, propExtra);
  }

  if (effectsPane) renderEffectsPanel(effectsPane, item, ctx);
  if (animationsPane) renderAnimationsPanel(animationsPane, item, ctx);
  setPropsLockedState(propsForm, !!item.locked, effectsPane, animationsPane);
}

/** Disable all inspector controls except the lock checkbox when the item is locked. */
export function setPropsLockedState(
  propsForm: HTMLElement,
  locked: boolean,
  ...extraRoots: Array<HTMLElement | null | undefined>
): void {
  propsForm.classList.toggle("ccs-props-locked", locked);
  const roots: HTMLElement[] = [propsForm];
  for (const root of extraRoots) {
    if (root) {
      root.classList.toggle("ccs-props-locked", locked);
      roots.push(root);
    }
  }
  for (const root of roots) {
    root.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement | HTMLButtonElement>(
      "input, select, textarea, button"
    ).forEach((el) => {
      if (el.id === "propLocked") {
        el.disabled = false;
        return;
      }
      el.disabled = locked;
    });
  }
}

function appendMusicProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = window.CcsCanvas.MUSIC_VARIANTS || [];
  const labels = window.CcsCanvas.MUSIC_VARIANT_LABELS || {};
  const sizePresets = window.CcsCanvas.MUSIC_SIZE_PRESETS || {};

  const content = contentSection("music");
  content.body.appendChild(boolProp("showTitle", "Titel", item, ctx));
  content.body.appendChild(boolProp("showArtist", "Artist", item, ctx));
  content.body.appendChild(boolProp("showAlbumCover", "Cover", item, ctx));
  content.body.appendChild(boolProp("showProgress", "Progress", item, ctx));
  content.body.appendChild(boolProp("hideWhenPaused", "Bei Pause ausblenden", item, ctx));
  propExtra.appendChild(content.root);

  const look = lookSection("music", "Look & Größe");
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
}

function appendSocialsProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const content = contentSection("socials");
  content.body.appendChild(selectProp("platform", "Plattform", item, ctx, [
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
  content.body.appendChild(textProp("handle", "Handle", item, ctx, ""));
  content.body.appendChild(textProp("url", "URL (optional)", item, ctx, ""));
  content.body.appendChild(textProp("label", "Label (optional)", item, ctx, ""));
  content.body.appendChild(imageProp("iconUrl", "Icon-URL (optional)", item, ctx, ""));
  content.body.appendChild(selectProp("variant", "Variante", item, ctx, [
    { value: "row", label: "Row" },
    { value: "pills", label: "Pills" },
    { value: "cards", label: "Cards" },
    { value: "stack", label: "Stack" },
    { value: "neon", label: "Neon" },
    { value: "minimal", label: "Minimal" }
  ], "pills"));
  content.body.appendChild(selectProp("iconLibrary", "Icons", item, ctx, [
    { value: "svg", label: "Built-in SVG" },
    { value: "fontawesome", label: "Font Awesome" }
  ], "svg"));
  content.body.appendChild(boolProp("showLabels", "Labels zeigen", item, ctx));
  content.body.appendChild(boolProp("showHandles", "Handles zeigen", item, ctx));
  propExtra.appendChild(content.root);

  const style = styleSection("socials");
  style.body.appendChild(selectProp("colorMode", "Farben", item, ctx, [
    { value: "brand", label: "Brand" },
    { value: "mono", label: "Mono" }
  ], "brand"));
  style.body.appendChild(numProp("iconSize", "Icon-Größe px", item, ctx, 36));
  style.body.appendChild(numProp("gap", "Abstand px", item, ctx, 12));
  style.body.appendChild(colorProp("iconColor", "Mono-Farbe", item, ctx, "#ffffff"));
  propExtra.appendChild(style.root);
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
  const timing = contentSection("partner-roulette", "Timing & Übergang");
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

  const style = styleSection("partner-roulette", "Darstellung");
  style.body.appendChild(selectProp("fit", "Einpassung", item, ctx, [
    { value: "contain", label: "Contain" },
    { value: "cover", label: "Cover" },
    { value: "fill", label: "Fill" },
    { value: "none", label: "None" },
    { value: "scale-down", label: "Scale-down" }
  ], "contain"));
  style.body.appendChild(numProp("borderRadiusPx", "Eckenradius px", item, ctx, 12));
  style.body.appendChild(selectProp("objectPosition", "Position", item, ctx, [
    { value: "center", label: "Mitte" },
    { value: "top", label: "Oben" },
    { value: "bottom", label: "Unten" },
    { value: "left", label: "Links" },
    { value: "right", label: "Rechts" },
    { value: "top left", label: "Oben links" },
    { value: "top right", label: "Oben rechts" },
    { value: "bottom left", label: "Unten links" },
    { value: "bottom right", label: "Unten rechts" }
  ], "center"));
  propExtra.appendChild(style.root);

  const imagesSection = propSection("partner-roulette-images", "Bilder", false);
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
      row.appendChild(
        attachImageLibraryButton(input, (url) => {
          ctx.commitProp(item, (next) => {
            const nextImages = readRouletteImages(next);
            nextImages[i] = url;
            next.props.images = nextImages;
          });
        })
      );
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

  const content = contentSection("frame");
  content.body.appendChild(selectProp("mode", "Modus", item, ctx, modeOptions, modeFallback, (live, value) => {
    live.props.mode = value;
    // Normalize legacy shape ids to the unified frame type once the user picks a mode.
    if (live.type !== "frame") live.type = "frame";
  }));
  propExtra.appendChild(content.root);

  const style = styleSection("frame");
  style.body.appendChild(colorProp("color", "Farbe", item, ctx, "#ff7a00"));
  style.body.appendChild(numProp("radius", "Eckenradius px", item, ctx, 16));
  propExtra.appendChild(style.root);
}

function appendFrameCardProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const sizePresets = window.CcsCanvas.CARD_FRAME_SIZE_PRESETS || {};
  const sizeOptions = Object.keys(sizePresets).map((key) => ({
    value: key,
    label: (sizePresets as Record<string, { label?: string }>)[key].label || key
  }));

  const content = contentSection("frame-card");
  content.body.appendChild(selectProp("variant", "Variante", item, ctx, [
    { value: "classic", label: "Classic" },
    { value: "neon", label: "Neon" },
    { value: "soft", label: "Soft" },
    { value: "bold", label: "Bold" },
    { value: "outline", label: "Outline" },
    { value: "glass", label: "Glass" },
    { value: "cyber", label: "Cyber" },
    { value: "minimal", label: "Minimal" }
  ], "classic"));
  content.body.appendChild(selectProp("sizePreset", "Größe", item, ctx, sizeOptions, "metaschutz", (live, value) => {
    live.props.sizePreset = value;
    const next = (sizePresets as Record<string, { w: number; h: number }>)[value];
    if (!next) return;
    live.w = next.w;
    live.h = next.h;
  }));
  propExtra.appendChild(content.root);

  const style = styleSection("frame-card");
  style.body.appendChild(colorProp("color", "Farbe", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("color2", "Farbe 2", item, ctx, "#ffb36b"));
  style.body.appendChild(numProp("fillOpacity", "Fill Opacity", item, ctx, 0.18));
  propExtra.appendChild(style.root);

  const advanced = advancedSection("frame-card");
  advanced.body.appendChild(boolProp("showSweep", "Sweep", item, ctx));
  advanced.body.appendChild(boolProp("showLines", "Linien", item, ctx));
  propExtra.appendChild(advanced.root);
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

  const look = lookSection("scene-bg");
  look.body.appendChild(selectProp("preset", "Variation", item, ctx, presetOptions, "ember", (live, value) => {
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
  propExtra.appendChild(look.root);

  const style = styleSection("scene-bg");
  style.body.appendChild(colorProp("bgBase", "BG Basis", item, ctx, String(cfg.bgBase || "#030303")));
  style.body.appendChild(colorProp("bgMid", "BG Mitte", item, ctx, String(cfg.bgMid || "#101010")));
  style.body.appendChild(colorProp("bgDeep", "BG Tief", item, ctx, String(cfg.bgDeep || "#1a0d03")));
  style.body.appendChild(colorProp("glow1", "Glow 1", item, ctx, String(cfg.glow1 || "#ff7a00")));
  style.body.appendChild(colorProp("glow2", "Glow 2", item, ctx, String(cfg.glow2 || "#ffb36b")));
  style.body.appendChild(colorProp("stripeColor", "Streifen-Farbe", item, ctx, String(cfg.stripeColor || cfg.glow1 || "#ff7a00")));
  style.body.appendChild(colorProp("particleColor", "Partikel-Farbe", item, ctx, String(cfg.particleColor || cfg.glow1 || "#ff7a00")));
  style.body.appendChild(numProp("glow1Opacity", "Glow-1-Opacity", item, ctx, Number(cfg.glow1Opacity ?? 0.18)));
  style.body.appendChild(numProp("glow2Opacity", "Glow-2-Opacity", item, ctx, Number(cfg.glow2Opacity ?? 0.10)));
  style.body.appendChild(numProp("stripeOpacity", "Streifen-Opacity", item, ctx, Number(cfg.stripeOpacity ?? 0.065)));
  style.body.appendChild(numProp("particleOpacity", "Partikel-Opacity", item, ctx, Number(cfg.particleOpacity ?? 0.34)));
  style.body.appendChild(numProp("speed", "Geschwindigkeit", item, ctx, Number(cfg.speed ?? 1)));
  style.body.appendChild(numProp("driftDuration", "Drift (s)", item, ctx, Number(cfg.driftDuration ?? 18)));
  style.body.appendChild(numProp("particleDuration", "Partikel (s)", item, ctx, Number(cfg.particleDuration ?? 22)));
  style.body.appendChild(numProp("vignetteOpacity", "Vignette", item, ctx, Number(cfg.vignetteOpacity ?? 0)));
  style.body.appendChild(numProp("scanOpacity", "Scanline", item, ctx, Number(cfg.scanOpacity ?? 0)));
  propExtra.appendChild(style.root);

  const advanced = advancedSection("scene-bg");
  advanced.body.appendChild(boolProp("stripes", "Streifen", item, ctx));
  advanced.body.appendChild(boolProp("particles", "Partikel", item, ctx));
  advanced.body.appendChild(boolProp("paused", "Pause", item, ctx));
  propExtra.appendChild(advanced.root);
}
