import { extUrl } from "./ext-url";

interface PackFont {
  family?: string;
  Family?: string;
  src?: string;
  Src?: string;
  weight?: string;
  Weight?: string;
  style?: string;
  Style?: string;
}

interface PackWidget {
  id?: string;
  Id?: string;
  entry?: string;
  Entry?: string;
  css?: string;
  Css?: string;
  style?: string;
}

interface PackEffect {
  id?: string;
  Id?: string;
  entry?: string;
  Entry?: string;
  style?: string;
  Style?: string;
  css?: string;
  Css?: string;
}

interface PackSummary {
  id: string;
  name?: string;
  widgets?: PackWidget[];
  effects?: PackEffect[];
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

function loadStylesheet(url: string): void {
  if (document.querySelector(`link[data-ccs-ext="${url}"]`)) return;
  const link = document.createElement("link");
  link.rel = "stylesheet";
  link.href = url;
  link.dataset.ccsExt = url;
  document.head.appendChild(link);
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

export async function loadExtensions(): Promise<void> {
  try {
    const res = await fetch("/extensions", { cache: "no-store" });
    if (!res.ok) return;
    const data = (await res.json()) as CatalogResponse | PackSummary[];
    const packs = Array.isArray(data) ? data : (data.packs || []);
    for (const pack of packs) {
      if (!pack || !pack.id) continue;
      registerPackFonts(pack.id, pack.fonts);
      for (const widget of pack.widgets || []) {
        const entry = widget.entry || widget.Entry;
        const css = widget.css || widget.Css || widget.style;
        if (css) loadStylesheet(extUrl(pack.id, css));
        if (entry) {
          try {
            await loadScript(extUrl(pack.id, entry));
          } catch {
            /* optional */
          }
        }
      }
      for (const effect of pack.effects || []) {
        const entry = effect.entry || effect.Entry;
        const css = effect.css || effect.Css || effect.style || effect.Style;
        if (css) loadStylesheet(extUrl(pack.id, css));
        if (entry) {
          try {
            await loadScript(extUrl(pack.id, entry));
          } catch {
            /* optional */
          }
        }
      }
    }
  } catch {
    /* extensions endpoint optional */
  }
}

export { extUrl };
export { registerWidget } from "./registry";
export { registerEffect } from "../effects/registry";
