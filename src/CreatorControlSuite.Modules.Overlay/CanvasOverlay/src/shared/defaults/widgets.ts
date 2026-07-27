import type { WidgetDefaults } from "../types";

export const WIDGET_DEFAULTS: Record<string, WidgetDefaults> = {
  online: { w: 280, h: 90, props: { showClock: true, showUptime: true } },
  alert: { w: 480, h: 140, props: { durationMs: 5000, maxQueue: 5 } },
  music: {
    w: 950,
    h: 188,
    props: {
      showTitle: true,
      showArtist: true,
      showAlbumCover: true,
      showProgress: true,
      hideWhenPaused: false
    }
  },
  chat: {
    w: 420,
    h: 560,
    props: {
      showTwitchEvents: true,
      maxLines: 80,
      backgroundType: "None",
      backgroundColor: "#000000",
      backgroundOpacityPercent: 55,
      paddingPx: 12,
      borderRadiusPx: 12,
      gapPx: 6,
      fontSizePx: 18,
      fontFamily: "Segoe UI, system-ui, sans-serif"
    }
  },
  "ending-stats": {
    w: 980,
    h: 220,
    props: {
      variant: "classic",
      showTitle: true
    }
  },
  text: {
    w: 480,
    h: 120,
    props: {
      content: "Text",
      fontSizePx: 48,
      fontFamily: "Segoe UI, system-ui, sans-serif",
      color: "#ffffff",
      align: "center",
      verticalAlign: "middle",
      fontWeight: "700",
      letterSpacingPx: 0,
      lineHeight: 1.15,
      textShadow: "0 2px 12px rgba(0,0,0,.55)"
    }
  },
  image: {
    w: 400,
    h: 400,
    props: {
      src: "",
      fit: "contain",
      opacity: 1,
      borderRadiusPx: 0,
      objectPosition: "center"
    }
  },
  countdown: {
    w: 520,
    h: 160,
    props: {
      variant: "classic",
      format: "mm:ss",
      showLabel: true,
      hideWhenIdle: false,
      fontSizePx: 72,
      color: "#ffffff",
      align: "center"
    }
  },
  socials: {
    w: 280,
    h: 72,
    props: {
      platform: "twitch",
      handle: "",
      url: "",
      iconUrl: "",
      label: "",
      variant: "pills",
      showLabels: true,
      showHandles: true,
      colorMode: "brand",
      iconLibrary: "svg",
      iconSize: 36,
      gap: 12,
      iconColor: "#ffffff"
    }
  },
  spotify: {
    w: 950,
    h: 188,
    props: {
      showTitle: true,
      showArtist: true,
      showAlbumCover: true,
      showProgress: true,
      hideWhenPaused: false
    }
  }
};
