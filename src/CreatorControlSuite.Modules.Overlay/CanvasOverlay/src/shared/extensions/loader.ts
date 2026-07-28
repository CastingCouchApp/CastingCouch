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

function loadScript(url: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const existing = document.querySelector(`script[data-ccs-ext="${url}"]`);
    if (existing) {
      resolve();
      return;
    }
    const script = document.createElement("script");
    script.src = url;
    script.async = true;
    script.dataset.ccsExt = url;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Failed to load " + url));
    document.head.appendChild(script);
  });
}

function loadStylesheet(url: string): Promise<void> {
  return new Promise((resolve) => {
    const existing = document.querySelector(`link[data-ccs-ext="${url}"]`) as HTMLLinkElement | null;
    if (existing) {
      resolve();
      return;
    }
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = url;
    link.dataset.ccsExt = url;
    link.onload = () => resolve();
    link.onerror = () => resolve(); // optional asset
    document.head.appendChild(link);
  });
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
    const url = extUrl(packId, src);
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
    const res = await fetch("/extensions", { cache: "no-store" });
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
