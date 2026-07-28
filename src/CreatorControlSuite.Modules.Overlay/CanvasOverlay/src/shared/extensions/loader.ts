import { extUrl } from "./ext-url";

export interface PackFont {
  family?: string;
  Family?: string;
  src?: string;
  Src?: string;
  weight?: string;
  Weight?: string;
  style?: string;
  Style?: string;
}

export interface PackWidget {
  id?: string;
  Id?: string;
  name?: string;
  Name?: string;
  entry?: string;
  Entry?: string;
  css?: string;
  Css?: string;
  style?: string;
  Style?: string;
}

export interface PackEffect {
  id?: string;
  Id?: string;
  name?: string;
  Name?: string;
  entry?: string;
  Entry?: string;
  style?: string;
  Style?: string;
  css?: string;
  Css?: string;
}

export interface PackAnimation {
  id?: string;
  Id?: string;
  name?: string;
  Name?: string;
  entry?: string;
  Entry?: string;
  style?: string;
  Style?: string;
  css?: string;
  Css?: string;
}

export interface PackSummary {
  id: string;
  name?: string;
  widgets?: PackWidget[];
  effects?: PackEffect[];
  animations?: PackAnimation[];
  fonts?: PackFont[];
}

interface CatalogResponse {
  packs?: PackSummary[];
}

/** Absolute URL — relative paths are unreliable in some OBS CEF builds. */
export function absoluteUrl(path: string): string {
  if (!path) return path;
  if (/^[a-z][a-z0-9+.-]*:/i.test(path)) return path;
  try {
    if (typeof location !== "undefined" && location.href) {
      return new URL(path, location.href).href;
    }
  } catch {
    /* ignore */
  }
  return path;
}

/**
 * Rewrite relative url(...) in pack CSS so fetch-injected <style> still resolves
 * against the stylesheet location (not the overlay page).
 */
export function rewriteCssUrls(css: string, cssFileUrl: string): string {
  return css.replace(/url\(\s*(['"]?)([^'")]+)\1\s*\)/gi, (match, quote: string, ref: string) => {
    const trimmed = (ref || "").trim();
    if (!trimmed || /^(data:|https?:|blob:|\/)/i.test(trimmed)) return match;
    try {
      return `url(${quote}${new URL(trimmed, cssFileUrl).href}${quote})`;
    } catch {
      return match;
    }
  });
}

/**
 * OBS Browser Source (CEF) often never fires <script src> / <link> onload.
 * Fetch + inline inject executes reliably and does not hang the boot chain.
 */
async function loadScript(url: string): Promise<void> {
  const abs = absoluteUrl(url);
  if (document.querySelector(`script[data-ccs-ext="${abs}"]`)) {
    return;
  }
  const res = await fetch(abs, { cache: "no-store" });
  if (!res.ok) {
    throw new Error("Failed to load " + abs);
  }
  const code = await res.text();
  const script = document.createElement("script");
  script.type = "text/javascript";
  script.dataset.ccsExt = abs;
  script.text = code;
  document.head.appendChild(script);
}

async function loadStylesheet(url: string): Promise<void> {
  const abs = absoluteUrl(url);
  if (document.querySelector(`[data-ccs-ext="${abs}"]`)) {
    return;
  }
  try {
    const res = await fetch(abs, { cache: "no-store" });
    if (!res.ok) return;
    const css = rewriteCssUrls(await res.text(), abs);
    const style = document.createElement("style");
    style.dataset.ccsExt = abs;
    style.textContent = css;
    document.head.appendChild(style);
  } catch {
    /* optional */
  }
}

function registerPackFonts(packId: string, fonts: PackFont[] | undefined): void {
  if (!fonts || !fonts.length) return;
  let css = "";
  for (const font of fonts) {
    const family = font.family || font.Family;
    const src = font.src || font.Src;
    if (!family || !src) continue;
    const weight = font.weight || font.Weight || "400";
    const style = font.style || font.Style || "normal";
    const url = absoluteUrl(extUrl(packId, src));
    css += `@font-face{font-family:${JSON.stringify(family)};src:url(${JSON.stringify(url)}) format("woff2");font-weight:${weight};font-style:${style};font-display:swap;}`;
    try {
      const w = window as unknown as { __ccsPackFonts?: string[] };
      const list = w.__ccsPackFonts || [];
      if (!list.includes(family)) list.push(family);
      w.__ccsPackFonts = list;
    } catch {
      /* ignore */
    }
  }
  if (!css) return;
  const styleEl = document.createElement("style");
  styleEl.dataset.ccsExtFonts = packId;
  styleEl.textContent = css;
  document.head.appendChild(styleEl);
}

async function loadPackEntries(
  packId: string,
  entries: Array<PackWidget | PackEffect | PackAnimation> | undefined
): Promise<void> {
  for (const entry of entries || []) {
    const scriptPath = entry.entry || entry.Entry;
    const css = entry.css || entry.Css || entry.style || entry.Style;
    if (css) {
      await loadStylesheet(extUrl(packId, css));
    }
    if (scriptPath) {
      try {
        await loadScript(extUrl(packId, scriptPath));
      } catch {
        /* optional */
      }
    }
  }
}

/**
 * Fetches `/extensions`, registers pack fonts, and loads widget/effect/animation
 * entry scripts. Returns the catalog packs (empty on failure) for editor palettes.
 */
export async function loadExtensions(): Promise<PackSummary[]> {
  try {
    const res = await fetch(absoluteUrl("/extensions"), { cache: "no-store" });
    if (!res.ok) return [];
    const data = (await res.json()) as CatalogResponse | PackSummary[];
    const packs = Array.isArray(data) ? data : (data.packs || []);
    for (const pack of packs) {
      if (!pack || !pack.id) continue;
      registerPackFonts(pack.id, pack.fonts);
      await loadPackEntries(pack.id, pack.widgets);
      await loadPackEntries(pack.id, pack.effects);
      await loadPackEntries(pack.id, pack.animations);
    }
    return packs.filter((pack) => !!pack?.id);
  } catch {
    /* extensions endpoint optional */
    return [];
  }
}

export { extUrl };
export { registerWidget } from "./registry";
export { registerEffect } from "../effects/registry";
export { registerAnimation } from "../animations/registry";
