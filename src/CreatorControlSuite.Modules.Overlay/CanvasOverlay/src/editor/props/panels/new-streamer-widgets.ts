import type { LayoutItem } from "../../../shared/types";
import type { EditorContext } from "../context";
import { boolProp } from "../../controls/bool-prop";
import { numProp } from "../../controls/num-prop";
import { textProp } from "../../controls/text-prop";
import { selectProp } from "../../controls/select-prop";
import { fontProp } from "../../controls/font-prop";
import { colorProp } from "../../controls/color-prop";
import {
  advancedSection,
  contentSection,
  featureSection,
  lookSection,
  styleSection
} from "../../sections/prop-section";

type SizeMap = Record<string, { w: number; h: number; label?: string }>;

function variantOptions(keys: readonly string[] | string[]): { value: string; label: string }[] {
  return keys.map((key) => ({
    value: key,
    label: key
      .split("-")
      .map((p) => p.charAt(0).toUpperCase() + p.slice(1))
      .join(" ")
  }));
}

function buildLookSection(
  id: string,
  item: LayoutItem,
  ctx: EditorContext,
  variants: readonly string[] | string[],
  sizePresets: SizeMap,
  defaultVariant = "classic",
  defaultSize = "standard"
): { root: HTMLDetailsElement; body: HTMLDivElement } {
  const look = lookSection(id);
  look.body.appendChild(
    selectProp("variant", "Style", item, ctx, variantOptions(variants), defaultVariant)
  );
  look.body.appendChild(
    selectProp(
      "sizePreset",
      "Größe",
      item,
      ctx,
      Object.keys(sizePresets).map((key) => ({
        value: key,
        label: sizePresets[key].label || key
      })),
      defaultSize,
      (live, value) => {
        live.props.sizePreset = value;
        const next = sizePresets[value];
        if (!next) return;
        live.w = next.w;
        live.h = next.h;
      }
    )
  );
  return look;
}

function appendAdvanced(
  id: string,
  propExtra: HTMLElement,
  features: HTMLElement[]
): void {
  if (!features.length) return;
  const advanced = advancedSection(id);
  for (const feature of features) advanced.body.appendChild(feature);
  propExtra.appendChild(advanced.root);
}

function featureToggle(
  id: string,
  title: string,
  enabledKey: string,
  item: LayoutItem,
  ctx: EditorContext,
  children?: (body: HTMLElement) => void
): HTMLElement {
  // Simple on/off → same row style as boolProp ("Inner Glow").
  if (!children) {
    return boolProp(enabledKey, title, item, ctx);
  }
  return featureSection({
    id,
    title,
    enabledKey,
    item,
    commit: (apply) => {
      ctx.commitProp(item, apply as (live: LayoutItem) => void);
    },
    children
  });
}

export function appendGoalBarProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { GOAL_BAR_VARIANTS?: string[] }).GOAL_BAR_VARIANTS || [
    "classic", "neon", "glass", "cyber", "minimal", "bold", "soft", "outline", "hud", "pixel", "stripe", "capsule"
  ];
  const sizes = (window.CcsCanvas as { GOAL_BAR_SIZE_PRESETS?: SizeMap }).GOAL_BAR_SIZE_PRESETS || {
    mini: { w: 280, h: 56, label: "Mini" },
    compact: { w: 420, h: 72, label: "Compact" },
    standard: { w: 560, h: 88, label: "Standard" },
    wide: { w: 760, h: 96, label: "Wide" },
    banner: { w: 920, h: 64, label: "Banner" }
  };

  const content = contentSection("goal-bar");
  content.body.appendChild(selectProp("kind", "Ziel-Typ", item, ctx, [
    { value: "followers", label: "Follower" },
    { value: "subs", label: "Subs" },
    { value: "bits", label: "Bits" },
    { value: "custom", label: "Custom" }
  ], "followers"));
  content.body.appendChild(textProp("label", "Label", item, ctx, "Follower Goal"));
  content.body.appendChild(numProp("target", "Ziel", item, ctx, 200));
  content.body.appendChild(numProp("current", "Aktuell (Override)", item, ctx, 0));
  content.body.appendChild(boolProp("showLabel", "Label", item, ctx));
  content.body.appendChild(boolProp("showCurrent", "Aktuell", item, ctx));
  content.body.appendChild(boolProp("showTarget", "Zielwert", item, ctx));
  content.body.appendChild(boolProp("showPercent", "Prozent", item, ctx));
  content.body.appendChild(boolProp("showRemaining", "Rest", item, ctx));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("goal-bar", item, ctx, variants, sizes).root);

  const style = styleSection("goal-bar");
  style.body.appendChild(selectProp("fillStyle", "Fill", item, ctx, [
    { value: "solid", label: "Solid" },
    { value: "gradient", label: "Gradient" },
    { value: "striped", label: "Striped" }
  ], "gradient"));
  style.body.appendChild(numProp("barHeightPx", "Bar-Höhe px", item, ctx, 14));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 10));
  style.body.appendChild(numProp("fontSizePx", "Schrift px", item, ctx, 16));
  style.body.appendChild(fontProp("fontFamily", "Schrift", item, ctx));
  style.body.appendChild(colorProp("color", "Akzent", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("color2", "Fill 2", item, ctx, "#ffb36b"));
  style.body.appendChild(colorProp("trackColor", "Track", item, ctx, "rgba(255,255,255,.12)"));
  style.body.appendChild(colorProp("textColor", "Text", item, ctx, "#ffffff"));
  propExtra.appendChild(style.root);

  appendAdvanced("goal-bar", propExtra, [
    featureToggle("goal-bar-animate", "Fill animieren", "animateFill", item, ctx),
    featureToggle("goal-bar-hide-complete", "Ausblenden wenn fertig", "hideWhenComplete", item, ctx),
    featureToggle("goal-bar-pulse", "Pulse bei Fortschritt", "pulseOnProgress", item, ctx)
  ]);
}

export function appendEventTickerProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { EVENT_TICKER_VARIANTS?: string[] }).EVENT_TICKER_VARIANTS || [
    "classic", "neon", "glass", "cyber", "minimal", "bold", "pill", "strip", "hud", "marquee-only", "cards", "chips"
  ];
  const sizes = (window.CcsCanvas as { EVENT_TICKER_SIZE_PRESETS?: SizeMap }).EVENT_TICKER_SIZE_PRESETS || {
    slim: { w: 960, h: 40, label: "Slim" },
    standard: { w: 1100, h: 56, label: "Standard" },
    tall: { w: 1100, h: 80, label: "Tall" },
    banner: { w: 1400, h: 64, label: "Banner" }
  };

  const content = contentSection("event-ticker");
  content.body.appendChild(numProp("maxItems", "Max. Items", item, ctx, 20));
  content.body.appendChild(selectProp("order", "Reihenfolge", item, ctx, [
    { value: "newest", label: "Neueste zuerst" },
    { value: "fifo", label: "FIFO" }
  ], "newest"));
  content.body.appendChild(textProp("template", "Template", item, ctx, "{user} · {type}"));
  content.body.appendChild(textProp("separator", "Trenner", item, ctx, "  •  "));
  content.body.appendChild(boolProp("showIcon", "Icon", item, ctx));
  content.body.appendChild(boolProp("showType", "Typ", item, ctx));
  content.body.appendChild(boolProp("showTime", "Zeit", item, ctx));
  content.body.appendChild(selectProp("mode", "Modus", item, ctx, [
    { value: "marquee", label: "Marquee" },
    { value: "fade-cycle", label: "Fade-Cycle" },
    { value: "static-list", label: "Liste" }
  ], "marquee"));
  content.body.appendChild(numProp("speed", "Geschwindigkeit", item, ctx, 40));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("event-ticker", item, ctx, variants, sizes).root);

  const style = styleSection("event-ticker");
  style.body.appendChild(fontProp("fontFamily", "Schrift", item, ctx));
  style.body.appendChild(numProp("fontSizePx", "Schrift px", item, ctx, 16));
  style.body.appendChild(colorProp("color", "Text", item, ctx, "#ffffff"));
  style.body.appendChild(colorProp("color2", "Akzent", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("bgColor", "Hintergrund", item, ctx, "#111111"));
  style.body.appendChild(numProp("bgOpacity", "BG Opacity", item, ctx, 0.55));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 10));
  style.body.appendChild(numProp("gapPx", "Gap px", item, ctx, 12));
  style.body.appendChild(numProp("paddingPx", "Padding px", item, ctx, 10));
  propExtra.appendChild(style.root);

  appendAdvanced("event-ticker", propExtra, [
    featureToggle("event-ticker-hide-empty", "Leer ausblenden", "hideWhenEmpty", item, ctx),
    featureToggle("event-ticker-uppercase", "Uppercase", "uppercase", item, ctx),
    featureToggle("event-ticker-avatars", "Avatare", "showAvatars", item, ctx)
  ]);
}

export function appendViewerCountProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { VIEWER_COUNT_VARIANTS?: string[] }).VIEWER_COUNT_VARIANTS || [
    "classic", "neon", "glass", "cyber", "minimal", "bold", "soft", "outline", "hud", "badge", "stat-card", "inline"
  ];
  const sizes = (window.CcsCanvas as { VIEWER_COUNT_SIZE_PRESETS?: SizeMap }).VIEWER_COUNT_SIZE_PRESETS || {
    mini: { w: 140, h: 48, label: "Mini" },
    compact: { w: 200, h: 64, label: "Compact" },
    standard: { w: 260, h: 80, label: "Standard" },
    wide: { w: 360, h: 88, label: "Wide" }
  };

  const content = contentSection("viewer-count");
  content.body.appendChild(textProp("label", "Label", item, ctx, "Viewer"));
  content.body.appendChild(boolProp("showLabel", "Label", item, ctx));
  content.body.appendChild(boolProp("showIcon", "Icon", item, ctx));
  content.body.appendChild(boolProp("showPeak", "Peak", item, ctx));
  content.body.appendChild(boolProp("showDelta", "Delta", item, ctx));
  content.body.appendChild(selectProp("format", "Format", item, ctx, [
    { value: "plain", label: "Plain" },
    { value: "compact", label: "Compact (1.2k)" }
  ], "plain"));
  content.body.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
    { value: "flex-start", label: "Links" },
    { value: "center", label: "Mitte" },
    { value: "flex-end", label: "Rechts" }
  ], "center"));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("viewer-count", item, ctx, variants, sizes).root);

  const style = styleSection("viewer-count");
  style.body.appendChild(fontProp("fontFamily", "Schrift", item, ctx));
  style.body.appendChild(numProp("fontSizePx", "Schrift px", item, ctx, 28));
  style.body.appendChild(numProp("iconSize", "Icon px", item, ctx, 22));
  style.body.appendChild(colorProp("color", "Text", item, ctx, "#ffffff"));
  style.body.appendChild(colorProp("color2", "Akzent", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("bgColor", "Hintergrund", item, ctx, "#111111"));
  style.body.appendChild(numProp("bgOpacity", "BG Opacity", item, ctx, 0.45));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 12));
  propExtra.appendChild(style.root);

  appendAdvanced("viewer-count", propExtra, [
    featureToggle("viewer-count-offline", "Offline ausblenden", "hideWhenOffline", item, ctx),
    featureToggle("viewer-count-pulse", "Pulse bei Änderung", "pulseOnChange", item, ctx)
  ]);
}

export function appendLowerThirdProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { LOWER_THIRD_VARIANTS?: string[] }).LOWER_THIRD_VARIANTS || [
    "classic", "neon", "glass", "cyber", "minimal", "bold", "soft", "outline",
    "broadcast", "esport", "ribbon", "split", "boxed", "underline"
  ];
  const sizes = (window.CcsCanvas as { LOWER_THIRD_SIZE_PRESETS?: SizeMap }).LOWER_THIRD_SIZE_PRESETS || {
    compact: { w: 420, h: 96, label: "Compact" },
    standard: { w: 560, h: 120, label: "Standard" },
    wide: { w: 720, h: 140, label: "Wide" },
    banner: { w: 960, h: 128, label: "Banner" }
  };

  const content = contentSection("lower-third");
  content.body.appendChild(textProp("name", "Name", item, ctx, "Streamer"));
  content.body.appendChild(textProp("subtitle", "Untertitel", item, ctx, "Just Chatting"));
  content.body.appendChild(textProp("tag", "Tag", item, ctx, "LIVE"));
  content.body.appendChild(textProp("avatarUrl", "Avatar-URL", item, ctx, ""));
  content.body.appendChild(boolProp("showAvatar", "Avatar", item, ctx));
  content.body.appendChild(boolProp("showSubtitle", "Untertitel", item, ctx));
  content.body.appendChild(boolProp("showTag", "Tag", item, ctx));
  content.body.appendChild(boolProp("showAccentBar", "Akzentleiste", item, ctx));
  content.body.appendChild(selectProp("layout", "Layout", item, ctx, [
    { value: "left", label: "Links" },
    { value: "center", label: "Mitte" },
    { value: "right", label: "Rechts" }
  ], "left"));
  content.body.appendChild(selectProp("avatarShape", "Avatar-Form", item, ctx, [
    { value: "circle", label: "Kreis" },
    { value: "rounded", label: "Rounded" },
    { value: "square", label: "Square" }
  ], "circle"));
  content.body.appendChild(selectProp("accentPosition", "Akzent", item, ctx, [
    { value: "left", label: "Links" },
    { value: "top", label: "Oben" },
    { value: "bar", label: "Bar" }
  ], "left"));
  content.body.appendChild(selectProp("entrance", "Entrance", item, ctx, [
    { value: "none", label: "None" },
    { value: "slide", label: "Slide" },
    { value: "fade", label: "Fade" },
    { value: "wipe", label: "Wipe" }
  ], "none"));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("lower-third", item, ctx, variants, sizes).root);

  const style = styleSection("lower-third");
  style.body.appendChild(fontProp("nameFont", "Name-Font", item, ctx));
  style.body.appendChild(fontProp("subtitleFont", "Subtitle-Font", item, ctx));
  style.body.appendChild(numProp("nameSizePx", "Name px", item, ctx, 28));
  style.body.appendChild(numProp("subtitleSizePx", "Subtitle px", item, ctx, 16));
  style.body.appendChild(colorProp("color", "Akzent", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("textColor", "Name-Farbe", item, ctx, "#ffffff"));
  style.body.appendChild(colorProp("subtitleColor", "Subtitle-Farbe", item, ctx, "rgba(255,255,255,.75)"));
  style.body.appendChild(colorProp("tagColor", "Tag-Farbe", item, ctx, "#111111"));
  style.body.appendChild(colorProp("bgColor", "Hintergrund", item, ctx, "#111111"));
  style.body.appendChild(numProp("bgOpacity", "BG Opacity", item, ctx, 0.72));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 12));
  style.body.appendChild(numProp("paddingPx", "Padding px", item, ctx, 14));
  style.body.appendChild(numProp("gapPx", "Gap px", item, ctx, 12));
  propExtra.appendChild(style.root);
}

export function appendQrCodeProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { QR_CODE_VARIANTS?: string[] }).QR_CODE_VARIANTS || [
    "classic", "neon", "glass", "minimal", "bold", "soft", "framed", "badge-caption"
  ];
  const sizes = (window.CcsCanvas as { QR_CODE_SIZE_PRESETS?: SizeMap }).QR_CODE_SIZE_PRESETS || {
    sm: { w: 160, h: 200, label: "S" },
    md: { w: 220, h: 260, label: "M" },
    lg: { w: 280, h: 320, label: "L" },
    xl: { w: 360, h: 400, label: "XL" }
  };

  const content = contentSection("qr-code");
  content.body.appendChild(textProp("url", "URL", item, ctx, ""));
  content.body.appendChild(textProp("caption", "Caption", item, ctx, "Scan me"));
  content.body.appendChild(boolProp("showCaption", "Caption", item, ctx));
  content.body.appendChild(selectProp("captionPosition", "Caption-Pos", item, ctx, [
    { value: "below", label: "Unten" },
    { value: "above", label: "Oben" },
    { value: "overlay", label: "Overlay" }
  ], "below"));
  content.body.appendChild(selectProp("errorCorrection", "Fehlerkorrektur", item, ctx, [
    { value: "L", label: "L" },
    { value: "M", label: "M" },
    { value: "Q", label: "Q" },
    { value: "H", label: "H" }
  ], "M"));
  content.body.appendChild(numProp("quietZone", "Quiet Zone", item, ctx, 2));
  content.body.appendChild(textProp("logoUrl", "Logo-URL", item, ctx, ""));
  content.body.appendChild(boolProp("showLogo", "Logo", item, ctx));
  content.body.appendChild(boolProp("frame", "Rahmen", item, ctx));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("qr-code", item, ctx, variants, sizes, "classic", "md").root);

  const style = styleSection("qr-code");
  style.body.appendChild(colorProp("fg", "QR Vordergrund", item, ctx, "#111111"));
  style.body.appendChild(colorProp("bg", "QR Hintergrund", item, ctx, "#ffffff"));
  style.body.appendChild(colorProp("color", "Rahmen-Akzent", item, ctx, "#ff7a00"));
  style.body.appendChild(fontProp("fontFamily", "Caption-Font", item, ctx));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 12));
  style.body.appendChild(numProp("paddingPx", "Padding px", item, ctx, 12));
  propExtra.appendChild(style.root);

  appendAdvanced("qr-code", propExtra, [
    featureToggle("qr-code-invert", "Invertieren", "invert", item, ctx),
    featureToggle("qr-code-hide-empty", "Ohne URL ausblenden", "hideWhenEmptyUrl", item, ctx)
  ]);
}

export function appendBrbPanelProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { BRB_PANEL_VARIANTS?: string[] }).BRB_PANEL_VARIANTS || [
    "classic", "neon", "glass", "cyber", "minimal", "bold", "soft", "outline",
    "broadcast", "poster", "card", "split-hero", "hud", "tape"
  ];
  const sizes = (window.CcsCanvas as { BRB_PANEL_SIZE_PRESETS?: SizeMap }).BRB_PANEL_SIZE_PRESETS || {
    compact: { w: 640, h: 280, label: "Compact" },
    standard: { w: 860, h: 360, label: "Standard" },
    wide: { w: 1060, h: 420, label: "Wide" },
    poster: { w: 720, h: 900, label: "Poster" },
    brb: { w: 1060, h: 420, label: "BRB" }
  };

  const content = contentSection("brb-panel");
  content.body.appendChild(selectProp("mode", "Modus", item, ctx, [
    { value: "brb", label: "BRB" },
    { value: "starting", label: "Starting Soon" },
    { value: "tech-pause", label: "Tech Pause" },
    { value: "custom", label: "Custom" }
  ], "brb"));
  content.body.appendChild(textProp("title", "Titel", item, ctx, ""));
  content.body.appendChild(textProp("message", "Nachricht", item, ctx, ""));
  content.body.appendChild(textProp("icon", "Icon", item, ctx, ""));
  content.body.appendChild(textProp("iconUrl", "Icon-URL", item, ctx, ""));
  content.body.appendChild(boolProp("showTitle", "Titel", item, ctx));
  content.body.appendChild(boolProp("showMessage", "Nachricht", item, ctx));
  content.body.appendChild(boolProp("showIcon", "Icon", item, ctx));
  content.body.appendChild(boolProp("showProgressBar", "Countdown-Bar", item, ctx));
  content.body.appendChild(selectProp("countdownFormat", "Countdown-Format", item, ctx, [
    { value: "mm:ss", label: "mm:ss" },
    { value: "hh:mm:ss", label: "hh:mm:ss" },
    { value: "ss", label: "Sekunden" }
  ], "mm:ss"));
  content.body.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
    { value: "flex-start", label: "Links" },
    { value: "center", label: "Mitte" },
    { value: "flex-end", label: "Rechts" }
  ], "center"));
  content.body.appendChild(numProp("stackGap", "Gap px", item, ctx, 12));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("brb-panel", item, ctx, variants, sizes, "classic", "brb").root);

  const style = styleSection("brb-panel");
  style.body.appendChild(fontProp("titleFont", "Titel-Font", item, ctx));
  style.body.appendChild(fontProp("messageFont", "Message-Font", item, ctx));
  style.body.appendChild(numProp("titleSizePx", "Titel px", item, ctx, 48));
  style.body.appendChild(numProp("messageSizePx", "Message px", item, ctx, 20));
  style.body.appendChild(colorProp("color", "Akzent", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("textColor", "Titel-Farbe", item, ctx, "#ffffff"));
  style.body.appendChild(colorProp("messageColor", "Message-Farbe", item, ctx, "rgba(255,255,255,.8)"));
  style.body.appendChild(colorProp("bgColor", "Hintergrund", item, ctx, "#0b0b0b"));
  style.body.appendChild(numProp("bgOpacity", "BG Opacity", item, ctx, 0.72));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 16));
  propExtra.appendChild(style.root);

  appendAdvanced("brb-panel", propExtra, [
    featureToggle("brb-panel-countdown", "Countdown anzeigen", "showCountdown", item, ctx),
    featureToggle("brb-panel-uppercase", "Titel Uppercase", "uppercaseTitle", item, ctx),
    featureToggle("brb-panel-hide-idle", "Ausblenden wenn Countdown idle", "hideWhenCountdownIdle", item, ctx)
  ]);
}

export function appendAnnouncementBarProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { ANNOUNCEMENT_BAR_VARIANTS?: string[] }).ANNOUNCEMENT_BAR_VARIANTS || [
    "classic", "neon", "glass", "cyber", "minimal", "bold", "pill", "strip", "ribbon", "alert-soft", "sponsor", "schedule"
  ];
  const sizes = (window.CcsCanvas as { ANNOUNCEMENT_BAR_SIZE_PRESETS?: SizeMap }).ANNOUNCEMENT_BAR_SIZE_PRESETS || {
    slim: { w: 960, h: 48, label: "Slim" },
    standard: { w: 1200, h: 64, label: "Standard" },
    tall: { w: 1100, h: 88, label: "Tall" },
    banner: { w: 1400, h: 72, label: "Banner" }
  };

  const content = contentSection("announcement-bar");
  content.body.appendChild(textProp("message", "Nachricht", item, ctx, "Willkommen im Stream!"));
  content.body.appendChild(textProp("prefix", "Prefix", item, ctx, ""));
  content.body.appendChild(textProp("icon", "Icon", item, ctx, ""));
  content.body.appendChild(boolProp("showIcon", "Icon", item, ctx));
  content.body.appendChild(boolProp("showPrefix", "Prefix", item, ctx));
  content.body.appendChild(selectProp("direction", "Richtung", item, ctx, [
    { value: "ltr", label: "LTR" },
    { value: "rtl", label: "RTL" }
  ], "ltr"));
  content.body.appendChild(numProp("speed", "Speed", item, ctx, 40));
  content.body.appendChild(numProp("repeatGap", "Repeat-Gap", item, ctx, 48));
  content.body.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
    { value: "flex-start", label: "Links" },
    { value: "center", label: "Mitte" },
    { value: "flex-end", label: "Rechts" }
  ], "center"));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("announcement-bar", item, ctx, variants, sizes).root);

  const style = styleSection("announcement-bar");
  style.body.appendChild(fontProp("fontFamily", "Schrift", item, ctx));
  style.body.appendChild(numProp("fontSizePx", "Schrift px", item, ctx, 18));
  style.body.appendChild(colorProp("color", "Text", item, ctx, "#ffffff"));
  style.body.appendChild(colorProp("color2", "Akzent", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("bgColor", "Hintergrund", item, ctx, "#111111"));
  style.body.appendChild(numProp("bgOpacity", "BG Opacity", item, ctx, 0.65));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 10));
  style.body.appendChild(numProp("paddingPx", "Padding px", item, ctx, 12));
  propExtra.appendChild(style.root);

  appendAdvanced("announcement-bar", propExtra, [
    featureToggle("announcement-bar-scroll", "Scroll / Marquee", "scroll", item, ctx),
    featureToggle("announcement-bar-uppercase", "Uppercase", "uppercase", item, ctx),
    featureToggle("announcement-bar-accent", "Akzent-Punkt", "showAccentDot", item, ctx),
    featureToggle("announcement-bar-hide-empty", "Leer ausblenden", "hideWhenEmpty", item, ctx)
  ]);
}

export function appendAnimatedBackgroundProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const api = window.CcsCanvas as {
    ANIMATED_BACKGROUND_VARIANTS?: string[];
    ANIMATED_BACKGROUND_SIZE_PRESETS?: SizeMap;
    ANIMATED_BACKGROUND_VARIANT_LABELS?: Record<string, string>;
  };
  const variants = api.ANIMATED_BACKGROUND_VARIANTS || [
    "hacker", "cyber", "retro", "vaporwave", "meme", "queer", "peace", "street",
    "aurora", "ocean", "fire", "cosmic", "glitch", "pixel", "lava", "ice",
    "disco", "rain", "bubbles", "grid", "sunset", "forest", "candy", "noir",
    "mountains", "alpine", "fuji", "mesa", "neon-peaks", "mist-peaks",
    "lowpoly", "papercut", "floating", "ridge-storm"
  ];
  const labels = api.ANIMATED_BACKGROUND_VARIANT_LABELS || {};
  const sizes = api.ANIMATED_BACKGROUND_SIZE_PRESETS || {
    fullscreen: { w: 1920, h: 1080, label: "Fullscreen 1080p" },
    hd: { w: 1280, h: 720, label: "HD 720p" },
    square: { w: 900, h: 900, label: "Square" },
    banner: { w: 1920, h: 360, label: "Banner" },
    vertical: { w: 1080, h: 1920, label: "Vertical" }
  };

  const look = lookSection("animated-background");
  look.body.appendChild(
    selectProp(
      "variant",
      "Style",
      item,
      ctx,
      variants.map((key) => ({
        value: key,
        label: labels[key] || key.split("-").map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join(" ")
      })),
      "cyber"
    )
  );
  look.body.appendChild(
    selectProp(
      "sizePreset",
      "Größe",
      item,
      ctx,
      Object.keys(sizes).map((key) => ({
        value: key,
        label: sizes[key].label || key
      })),
      "fullscreen",
      (live, value) => {
        live.props.sizePreset = value;
        const next = sizes[value];
        if (!next) return;
        live.w = next.w;
        live.h = next.h;
      }
    )
  );
  propExtra.appendChild(look.root);

  const motion = contentSection("animated-background", "Motion");
  motion.body.appendChild(numProp("speed", "Speed", item, ctx, 1));
  motion.body.appendChild(numProp("intensity", "Intensity", item, ctx, 0.85));
  motion.body.appendChild(numProp("density", "Density", item, ctx, 1));
  motion.body.appendChild(numProp("opacity", "Opacity", item, ctx, 1));
  propExtra.appendChild(motion.root);

  const style = styleSection("animated-background", "Farben");
  style.body.appendChild(colorProp("color", "Farbe 1", item, ctx, "#00f0ff"));
  style.body.appendChild(colorProp("color2", "Farbe 2", item, ctx, "#ff00aa"));
  style.body.appendChild(colorProp("color3", "Farbe 3", item, ctx, "#7a00ff"));
  style.body.appendChild(numProp("borderRadiusPx", "Radius px", item, ctx, 0));
  propExtra.appendChild(style.root);

  appendAdvanced("animated-background", propExtra, [
    featureToggle("animated-background-vignette", "Vignette", "vignette", item, ctx),
    featureToggle("animated-background-paused", "Animation pausieren", "paused", item, ctx)
  ]);
}

export function appendDividerProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { DIVIDER_VARIANTS?: string[] }).DIVIDER_VARIANTS || [
    "line", "dashed", "dotted", "double", "gradient", "glow", "flourish", "bracket", "diamond", "chevron", "wave", "pixel"
  ];
  const sizes = (window.CcsCanvas as { DIVIDER_SIZE_PRESETS?: SizeMap }).DIVIDER_SIZE_PRESETS || {
    thin: { w: 400, h: 8, label: "Thin" },
    standard: { w: 600, h: 16, label: "Standard" },
    ornate: { w: 800, h: 32, label: "Ornate" }
  };

  const content = contentSection("divider");
  content.body.appendChild(selectProp("orientation", "Orientierung", item, ctx, [
    { value: "h", label: "Horizontal" },
    { value: "v", label: "Vertikal" }
  ], "h"));
  content.body.appendChild(numProp("thickness", "Dicke px", item, ctx, 2));
  content.body.appendChild(selectProp("lengthMode", "Länge", item, ctx, [
    { value: "fill", label: "Fill" },
    { value: "percent", label: "Percent" }
  ], "fill"));
  content.body.appendChild(numProp("lengthPercent", "Länge %", item, ctx, 80));
  content.body.appendChild(selectProp("align", "Ausrichtung", item, ctx, [
    { value: "flex-start", label: "Start" },
    { value: "center", label: "Mitte" },
    { value: "flex-end", label: "Ende" }
  ], "center"));
  content.body.appendChild(boolProp("showCenterMotif", "Center-Motif", item, ctx));
  content.body.appendChild(selectProp("motif", "Motif", item, ctx, [
    { value: "none", label: "None" },
    { value: "diamond", label: "Diamond" },
    { value: "star", label: "Star" },
    { value: "dot", label: "Dot" },
    { value: "custom", label: "Custom" }
  ], "diamond"));
  content.body.appendChild(numProp("motifSize", "Motif px", item, ctx, 18));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("divider", item, ctx, variants, sizes, "line", "standard").root);

  const style = styleSection("divider");
  style.body.appendChild(colorProp("color", "Farbe", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("color2", "Farbe 2", item, ctx, "#ffffff"));
  style.body.appendChild(numProp("opacity", "Opacity", item, ctx, 1));
  propExtra.appendChild(style.root);

  appendAdvanced("divider", propExtra, [
    featureToggle("divider-shimmer", "Shimmer", "animateShimmer", item, ctx)
  ]);
}

export function appendCamRingProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const variants = (window.CcsCanvas as { CAM_RING_VARIANTS?: string[] }).CAM_RING_VARIANTS || [
    "ring", "double-ring", "hex", "soft", "neon", "cyber", "pixel", "dashed", "corners", "badge-only", "orbit", "square-round"
  ];
  const sizes = (window.CcsCanvas as { CAM_RING_SIZE_PRESETS?: SizeMap }).CAM_RING_SIZE_PRESETS || {
    "cam-sm": { w: 220, h: 220, label: "Cam S" },
    "cam-md": { w: 320, h: 320, label: "Cam M" },
    "cam-lg": { w: 420, h: 420, label: "Cam L" },
    "cam-tall": { w: 280, h: 400, label: "Cam Tall" }
  };

  const content = contentSection("cam-ring");
  content.body.appendChild(numProp("strokeWidth", "Stroke px", item, ctx, 6));
  content.body.appendChild(numProp("gap", "Gap px", item, ctx, 8));
  content.body.appendChild(numProp("radius", "Radius px", item, ctx, 999));
  content.body.appendChild(selectProp("badge", "Badge", item, ctx, [
    { value: "none", label: "None" },
    { value: "live", label: "LIVE" },
    { value: "rec", label: "REC" },
    { value: "custom", label: "Custom" }
  ], "live"));
  content.body.appendChild(textProp("badgeText", "Badge-Text", item, ctx, "LIVE"));
  content.body.appendChild(selectProp("badgePosition", "Badge-Pos", item, ctx, [
    { value: "tl", label: "OL" },
    { value: "tr", label: "OR" },
    { value: "bl", label: "UL" },
    { value: "br", label: "UR" },
    { value: "top", label: "Oben" }
  ], "tr"));
  content.body.appendChild(boolProp("showInnerGlow", "Inner Glow", item, ctx));
  propExtra.appendChild(content.root);
  propExtra.appendChild(buildLookSection("cam-ring", item, ctx, variants, sizes, "ring", "cam-md").root);

  const style = styleSection("cam-ring");
  style.body.appendChild(colorProp("color", "Ring", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("color2", "Glow", item, ctx, "#ffb36b"));
  style.body.appendChild(colorProp("badgeColor", "Badge BG", item, ctx, "#e10600"));
  style.body.appendChild(colorProp("badgeTextColor", "Badge Text", item, ctx, "#ffffff"));
  propExtra.appendChild(style.root);

  appendAdvanced("cam-ring", propExtra, [
    featureToggle("cam-ring-pulse", "Pulse", "pulse", item, ctx),
    featureToggle("cam-ring-rotate", "Langsam drehen", "rotateSlow", item, ctx)
  ]);
}

export function appendStickerProps(item: LayoutItem, ctx: EditorContext, propExtra: HTMLElement): void {
  const presets = (window.CcsCanvas as { STICKER_PRESETS?: string[] }).STICKER_PRESETS || [
    "heart", "star", "fire", "sparkle", "crown", "skull", "controller", "chat-bubble", "raid", "bit", "thumbs-up", "custom"
  ];
  const variants = (window.CcsCanvas as { STICKER_VARIANTS?: string[] }).STICKER_VARIANTS || [
    "flat", "neon", "glass", "outline", "soft", "badge"
  ];

  const look = lookSection("sticker");
  look.body.appendChild(selectProp("variant", "Hülle", item, ctx, variantOptions(variants), "flat"));
  look.body.appendChild(selectProp("preset", "Preset", item, ctx, variantOptions(presets), "heart"));
  look.body.appendChild(textProp("src", "Custom-URL", item, ctx, ""));
  look.body.appendChild(selectProp("fit", "Fit", item, ctx, [
    { value: "contain", label: "Contain" },
    { value: "cover", label: "Cover" },
    { value: "fill", label: "Fill" }
  ], "contain"));
  propExtra.appendChild(look.root);

  const style = styleSection("sticker");
  style.body.appendChild(numProp("rotateDeg", "Rotation °", item, ctx, 0));
  style.body.appendChild(numProp("scale", "Scale", item, ctx, 1));
  style.body.appendChild(numProp("opacity", "Opacity", item, ctx, 1));
  style.body.appendChild(boolProp("flipX", "Flip X", item, ctx));
  style.body.appendChild(boolProp("flipY", "Flip Y", item, ctx));
  style.body.appendChild(colorProp("color", "Tint", item, ctx, "#ff7a00"));
  style.body.appendChild(colorProp("color2", "Akzent", item, ctx, "#ffb36b"));
  propExtra.appendChild(style.root);

  appendAdvanced("sticker", propExtra, [
    featureToggle("sticker-bob", "Bob", "bob", item, ctx, (body) => {
      body.appendChild(numProp("bobAmplitude", "Amplitude", item, ctx, 6));
      body.appendChild(numProp("bobSpeed", "Speed", item, ctx, 1));
    }),
    featureToggle("sticker-spin", "Spin", "spin", item, ctx, (body) => {
      body.appendChild(numProp("spinSpeed", "Speed", item, ctx, 1));
    }),
    featureToggle("sticker-pulse", "Pulse", "pulse", item, ctx, (body) => {
      body.appendChild(numProp("pulseAmplitude", "Amplitude", item, ctx, 0.08));
      body.appendChild(numProp("pulseSpeed", "Speed", item, ctx, 1));
    })
  ]);
}
