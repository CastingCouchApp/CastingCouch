import type { LayoutItem } from "../../types";
import { prop } from "../../utils/prop";
import { formatMs } from "../../utils/format";
import "./music.css";

export const MUSIC_VARIANTS = [
  "classic",
  "neon",
  "minimal",
  "glass",
  "bold",
  "outline",
  "cyber",
  "soft",
  "solid",
  "gradient",
  "vinyl",
  "hud",
  "pill",
  "stacked",
  "ticker",
  "aurora",
  "mono",
  "retro",
  "bubble",
  "stripe",
  "frost",
  "ember"
] as const;

export type MusicVariant = (typeof MUSIC_VARIANTS)[number];

export const MUSIC_VARIANT_LABELS: Record<MusicVariant, string> = {
  classic: "Classic",
  neon: "Neon",
  minimal: "Minimal",
  glass: "Glass",
  bold: "Bold",
  outline: "Outline",
  cyber: "Cyber",
  soft: "Soft",
  solid: "Solid",
  gradient: "Gradient",
  vinyl: "Vinyl",
  hud: "HUD",
  pill: "Pill",
  stacked: "Stacked",
  ticker: "Ticker",
  aurora: "Aurora",
  mono: "Mono",
  retro: "Retro",
  bubble: "Bubble",
  stripe: "Stripe",
  frost: "Frost",
  ember: "Ember"
};

export const MUSIC_SIZE_PRESETS: Record<string, { w: number; h: number; label: string; scale: number }> = {
  mini: { w: 360, h: 72, label: "Mini", scale: 0.55 },
  compact: { w: 520, h: 110, label: "Compact", scale: 0.7 },
  cozy: { w: 720, h: 150, label: "Cozy", scale: 0.85 },
  standard: { w: 950, h: 188, label: "Standard", scale: 1 },
  large: { w: 1100, h: 240, label: "Large", scale: 1.15 },
  xl: { w: 1280, h: 300, label: "XL", scale: 1.35 },
  banner: { w: 1200, h: 96, label: "Banner", scale: 0.72 },
  cover: { w: 420, h: 420, label: "Cover", scale: 1 }
};

type MusicEl = HTMLElement & {
  _progressBase?: number;
  _progressAt?: number;
  _duration?: number;
  _playing?: boolean;
  _ro?: ResizeObserver;
  _marqueeRo?: ResizeObserver;
};

function musicVariant(item: LayoutItem | null | undefined): MusicVariant {
  const raw = String(prop(item, "variant", "classic") || "classic").toLowerCase();
  return (MUSIC_VARIANTS as readonly string[]).includes(raw) ? (raw as MusicVariant) : "classic";
}

function musicSizeKey(item: LayoutItem | null | undefined): string {
  const raw = String(prop(item, "sizePreset", "standard") || "standard").toLowerCase();
  return MUSIC_SIZE_PRESETS[raw] ? raw : "standard";
}

export function resolveMusicState(data: Record<string, unknown> | null | undefined): Record<string, unknown> {
  const music = ((data && data.music) || {}) as Record<string, unknown>;
  const spotify = ((data && data.spotify) || {}) as Record<string, unknown>;
  const hasMusic = Boolean(music.title || music.artist || music.connected === true || music.provider);
  return hasMusic ? music : spotify;
}

export function providerHeading(music: Record<string, unknown>): string {
  const name = String(music.providerDisplayName || "").trim();
  if (name) {
    return name.toUpperCase() + " · NOW PLAYING";
  }
  const id = String(music.provider || "").toLowerCase();
  if (id === "ytmusic") {
    return "YOUTUBE MUSIC · NOW PLAYING";
  }
  if (id === "spotify") {
    return "SPOTIFY · NOW PLAYING";
  }
  return "MUSIC · NOW PLAYING";
}

function wrapMarquee(className: string, text: string): string {
  return (
    `<div class="${className} ccs-music-marquee">` +
    `<div class="ccs-music-marquee-inner">${text}</div>` +
    `</div>`
  );
}

export function applyMusicVariant(el: HTMLElement, item: LayoutItem): void {
  const variant = musicVariant(item);
  MUSIC_VARIANTS.forEach((name) => {
    el.classList.remove("ccs-spotify-v-" + name);
  });
  el.classList.add("ccs-spotify-v-" + variant);
  el.dataset.variant = variant;
}

export function applyMusicSize(el: HTMLElement, item: LayoutItem): void {
  const key = musicSizeKey(item);
  const preset = MUSIC_SIZE_PRESETS[key] || MUSIC_SIZE_PRESETS.standard;
  Object.keys(MUSIC_SIZE_PRESETS).forEach((name) => {
    el.classList.remove("ccs-spotify-s-" + name);
  });
  el.classList.add("ccs-spotify-s-" + key);
  el.dataset.sizePreset = key;
  el.style.setProperty("--ccs-music-preset-scale", String(preset.scale));
}

export function fitMusic(el: HTMLElement): void {
  if (!el) return;
  const w = Math.max(1, el.clientWidth || el.offsetWidth || 950);
  const h = Math.max(1, el.clientHeight || el.offsetHeight || 188);
  const baseScale = Number.parseFloat(getComputedStyle(el).getPropertyValue("--ccs-music-preset-scale")) || 1;
  const boxScale = Math.max(0.45, Math.min(1.45, Math.min(w / 950, h / 188)));
  const scale = Math.max(0.4, Math.min(1.6, baseScale * boxScale));
  el.style.setProperty("--ccs-music-scale", String(scale));
  el.style.setProperty("--ccs-music-w", w + "px");
  el.style.setProperty("--ccs-music-h", h + "px");
  el.classList.toggle("ccs-music-narrow", w < 520);
  el.classList.toggle("ccs-music-short", h < 120);
}

export function syncMusicMarquee(el: HTMLElement): void {
  if (!el) return;
  el.querySelectorAll<HTMLElement>(".ccs-music-marquee").forEach((track) => {
    const inner = track.querySelector<HTMLElement>(".ccs-music-marquee-inner");
    if (!inner) return;
    // Reset transform measurement
    track.classList.remove("is-scrolling");
    void track.offsetWidth;
    const overflow = inner.scrollWidth - track.clientWidth;
    const scrolling = overflow > 2;
    track.classList.toggle("is-scrolling", scrolling);
    if (scrolling) {
      track.style.setProperty("--ccs-marquee-distance", overflow + "px");
      const duration = Math.max(5, Math.min(28, overflow / 28));
      track.style.setProperty("--ccs-marquee-duration", duration + "s");
    } else {
      track.style.removeProperty("--ccs-marquee-distance");
      track.style.removeProperty("--ccs-marquee-duration");
    }
  });
}

function setMarqueeText(el: HTMLElement, selector: string, text: string): void {
  const track = el.querySelector<HTMLElement>(selector);
  if (!track) return;
  let inner = track.querySelector<HTMLElement>(".ccs-music-marquee-inner");
  if (!inner) {
    inner = document.createElement("div");
    inner.className = "ccs-music-marquee-inner";
    track.textContent = "";
    track.appendChild(inner);
  }
  if (inner.textContent !== text) {
    inner.textContent = text;
  }
}

export function createSpotifyEl(item?: LayoutItem): MusicEl {
  const el = document.createElement("div") as MusicEl;
  el.className = "ccs-spotify ccs-music";
  el.innerHTML =
    `<div class="ccs-spotify-content">` +
    `<div class="ccs-spotify-cover"></div>` +
    `<div class="ccs-spotify-info">` +
    `<div class="ccs-spotify-topline">` +
    wrapMarquee("ccs-spotify-heading", "MUSIC · NOW PLAYING") +
    `<div class="ccs-spotify-status">SPIELT</div>` +
    `</div>` +
    wrapMarquee("ccs-spotify-title", "-") +
    wrapMarquee("ccs-spotify-artist", "-") +
    wrapMarquee("ccs-spotify-album", "") +
    `<div class="ccs-spotify-progress-row">` +
    `<div class="ccs-spotify-progress-track"><div class="ccs-spotify-progress"></div></div>` +
    `<div class="ccs-spotify-time">00:00 / 00:00</div>` +
    `</div></div></div>`;
  el._progressBase = 0;
  el._progressAt = Date.now();
  el._duration = 0;
  el._playing = false;

  if (item) {
    applyMusicVariant(el, item);
    applyMusicSize(el, item);
  } else {
    el.classList.add("ccs-spotify-v-classic", "ccs-spotify-s-standard");
    el.style.setProperty("--ccs-music-preset-scale", "1");
  }

  if (typeof ResizeObserver !== "undefined") {
    el._ro = new ResizeObserver(() => {
      fitMusic(el);
      syncMusicMarquee(el);
    });
    el._ro.observe(el);
  }
  requestAnimationFrame(() => {
    fitMusic(el);
    syncMusicMarquee(el);
  });
  return el;
}

export function updateSpotify(
  el: MusicEl,
  item: LayoutItem,
  data: Record<string, unknown> | null | undefined
): void {
  const music = resolveMusicState(data);
  const showTitle = prop(item, "showTitle", music.showTitle !== false);
  const showArtist = prop(item, "showArtist", music.showArtist !== false);
  const showCover = prop(item, "showAlbumCover", music.showAlbumCover !== false);
  const showProgress = prop(item, "showProgress", music.showProgress !== false);
  const hideWhenPaused = prop(item, "hideWhenPaused", music.hideWhenPaused === true);

  const hasSong = Boolean(music.title || music.artist);
  const connected = music.connected === true;
  const showOverlay = music.showInOverlay !== false;
  let show = showOverlay && connected && hasSong;
  if (show && hideWhenPaused && !music.isPlaying) {
    show = false;
  }

  el.classList.toggle("visible", show);
  el.classList.toggle("paused", !music.isPlaying);

  applyMusicVariant(el, item);
  applyMusicSize(el, item);
  fitMusic(el);

  const content = el.querySelector(".ccs-spotify-content");
  content?.classList.toggle("no-cover", !showCover);
  const cover = el.querySelector<HTMLElement>(".ccs-spotify-cover");
  if (cover) {
    cover.style.display = showCover ? "" : "none";
    const coverUrl = String(music.cover || music.coverUrl || "");
    cover.style.backgroundImage = coverUrl ? `url("${coverUrl}")` : "none";
  }

  const titleEl = el.querySelector<HTMLElement>(".ccs-spotify-title");
  const artistEl = el.querySelector<HTMLElement>(".ccs-spotify-artist");
  const albumEl = el.querySelector<HTMLElement>(".ccs-spotify-album");
  const progressRow = el.querySelector<HTMLElement>(".ccs-spotify-progress-row");
  if (titleEl) titleEl.style.display = showTitle ? "" : "none";
  if (artistEl) artistEl.style.display = showArtist ? "" : "none";
  if (albumEl) albumEl.style.display = showArtist ? "" : "none";
  if (progressRow) progressRow.style.display = showProgress ? "" : "none";

  setMarqueeText(el, ".ccs-spotify-heading", providerHeading(music));
  setMarqueeText(el, ".ccs-spotify-title", String(music.title || "Unbekannter Titel"));
  setMarqueeText(el, ".ccs-spotify-artist", String(music.artist || "Unbekannter Künstler"));
  setMarqueeText(el, ".ccs-spotify-album", String(music.album || ""));

  const status = el.querySelector(".ccs-spotify-status");
  if (status) status.textContent = music.isPlaying ? "SPIELT" : "PAUSIERT";

  el._progressBase = Math.max(0, Number(music.progressMs) || 0);
  el._progressAt = Date.now();
  el._duration = Math.max(0, Number(music.durationMs) || 0);
  el._playing = music.isPlaying === true;
  paintSpotifyProgress(el);

  requestAnimationFrame(() => syncMusicMarquee(el));
}

export function paintSpotifyProgress(el: MusicEl): void {
  let current = el._progressBase || 0;
  if (el._playing) {
    current += Date.now() - (el._progressAt || Date.now());
  }
  const duration = el._duration || 0;
  const percent = duration > 0 ? Math.min(100, (current / duration) * 100) : 0;
  const bar = el.querySelector<HTMLElement>(".ccs-spotify-progress");
  if (bar) bar.style.width = `${percent}%`;
  const time = el.querySelector(".ccs-spotify-time");
  if (time) {
    time.textContent = `${formatMs(current)} / ${formatMs(duration)}`;
  }
}
