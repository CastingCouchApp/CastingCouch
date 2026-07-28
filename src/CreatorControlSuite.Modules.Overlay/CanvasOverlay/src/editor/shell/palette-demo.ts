import type { LayoutItem } from "../../shared/types";

/** Tiny SVG placeholders — no network required. */
export const PALETTE_DEMO_IMG =
  "data:image/svg+xml," +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240">` +
      `<rect width="240" height="240" fill="#1a1512"/>` +
      `<circle cx="120" cy="100" r="42" fill="#ff7a00"/>` +
      `<text x="120" y="180" text-anchor="middle" fill="#f3ece7" font-family="Segoe UI,sans-serif" font-size="22" font-weight="700">CCS</text>` +
      `</svg>`
  );

export const PALETTE_DEMO_COVER =
  "data:image/svg+xml," +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="200" height="200">` +
      `<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">` +
      `<stop stop-color="#ff7a00"/><stop offset="1" stop-color="#7c3aed"/></linearGradient></defs>` +
      `<rect width="200" height="200" fill="url(#g)"/>` +
      `<text x="100" y="108" text-anchor="middle" fill="#fff" font-family="Segoe UI,sans-serif" font-size="28" font-weight="700">♪</text>` +
      `</svg>`
  );

/** Overlay-data snapshot for live widgets (music, stats, countdown, …). */
export const PALETTE_DEMO_DATA: Record<string, unknown> = {
  stream: {
    isLive: true,
    elapsedSeconds: 3725,
    viewerCount: 1842
  },
  twitch: {
    followers: 1480,
    followerGoal: 2000
  },
  stats: {
    streamTimeSeconds: 3725,
    followersGained: 42,
    peakViewers: 2104,
    averageViewers: 1260,
    newSubscriptions: 18,
    giftSubscriptions: 6,
    chatMessages: 934,
    alertsPlayed: 27,
    bitsCheered: 4200
  },
  music: {
    connected: true,
    provider: "spotify",
    providerDisplayName: "Spotify",
    title: "Midnight Drive",
    artist: "Neon Horizon",
    album: "Afterglow",
    cover: PALETTE_DEMO_COVER,
    isPlaying: true,
    progressMs: 72000,
    durationMs: 214000,
    showInOverlay: true
  },
  countdown: {
    isRunning: true,
    label: "BRB",
    remainingSeconds: 185,
    durationSeconds: 300,
    endsAt: null
  }
};

const DEMO_PROPS: Record<string, Record<string, unknown>> = {
  text: { content: "Willkommen im Stream" },
  image: { src: PALETTE_DEMO_IMG },
  socials: { handle: "creator", label: "Twitch" },
  "partner-roulette": {
    images: [PALETTE_DEMO_IMG, PALETTE_DEMO_COVER, PALETTE_DEMO_IMG]
  },
  "announcement-bar": {
    message: "Folge für mehr Content · !discord · !socials",
    hideWhenEmpty: false
  },
  "bubatz-cantina": {
    title: "biomilchs Bubatz Cantina",
    subtitle: "Open late · Orbit Sector 7",
    message: "Blue Milk & Hyperspace Haze — heute happy hour",
    hideWhenEmpty: false
  },
  "fruppis-landadel": {
    name: "Peter Saul",
    title: "Anwalt",
    subtitle: "Cambridge · Landadel",
    tag: "ZWIELICHTIG",
    quote: "Weiße Schuhe, rote Hose, blauer Hoodie.",
    stats: "1,75 m · sportlich"
  },
  "qr-code": {
    url: "https://twitch.tv/creator",
    caption: "Folgen",
    hideWhenEmptyUrl: false
  },
  "lower-third": {
    name: "Creator",
    subtitle: "Just Chatting",
    tag: "LIVE",
    avatarUrl: PALETTE_DEMO_IMG,
    showTag: true
  },
  "brb-panel": {
    message: "Gleich zurück — Snack-Pause!",
    showCountdown: true
  },
  "goal-bar": { label: "Follower Goal" },
  "viewer-count": { label: "Viewer" },
  "event-ticker": { template: "{text}", hideWhenEmpty: false }
};

function isEmptyProp(value: unknown): boolean {
  if (value == null) return true;
  if (typeof value === "string" && value.trim() === "") return true;
  if (Array.isArray(value) && value.length === 0) return true;
  return false;
}

/** Fills blank widget props with demo content (does not overwrite user defaults that already have values). */
export function applyPaletteDemoProps(item: LayoutItem): LayoutItem {
  const demo = DEMO_PROPS[item.type];
  if (!demo) return item;
  const props = { ...(item.props || {}) };
  let changed = false;
  for (const [key, value] of Object.entries(demo)) {
    const cur = props[key];
    const placeholder =
      item.type === "text" && key === "content" && String(cur || "").trim() === "Text";
    if (isEmptyProp(cur) || placeholder) {
      props[key] = value;
      changed = true;
    }
  }
  return changed ? { ...item, props } : item;
}

export function demoChatMessages(): Array<Record<string, unknown>> {
  return [
    {
      messageId: "demo-1",
      userName: "PixelFox",
      color: "#ff7a00",
      parts: JSON.stringify([{ type: "text", text: "Heyo, geiler Stream!" }])
    },
    {
      messageId: "demo-2",
      userName: "NovaByte",
      color: "#7dd3fc",
      parts: JSON.stringify([{ type: "text", text: "Erstes Mal hier, was läuft?" }])
    },
    {
      messageId: "demo-3",
      userName: "MapleMod",
      color: "#86efac",
      parts: JSON.stringify([{ type: "text", text: "Willkommen an alle Neuen 🧡" }])
    }
  ];
}

export function demoAlertPayload(): Record<string, unknown> {
  return {
    alertType: "Follow",
    user: "PixelFox",
    summary: "ist jetzt Follower"
  };
}

export function demoTickerEvents(): Array<{ type: string; text: string }> {
  return [
    { type: "follow", text: "PixelFox · follow" },
    { type: "subscribe", text: "NovaByte · subscribe" },
    { type: "cheer", text: "MapleMod · cheer 500" }
  ];
}
