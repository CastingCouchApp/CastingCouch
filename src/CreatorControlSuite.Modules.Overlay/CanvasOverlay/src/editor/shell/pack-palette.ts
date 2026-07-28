import type { PaletteEntry } from "./palette";

export interface PackCatalogWidget {
  id?: string;
  Id?: string;
  name?: string;
  Name?: string;
}

export interface PackCatalogSummary {
  id?: string;
  Id?: string;
  name?: string;
  Name?: string;
  widgets?: PackCatalogWidget[];
  Widgets?: PackCatalogWidget[];
}

/** Runtime / palette type for a pack widget: `ext:{packId}:{widgetId}`. */
export function extType(packId: string, entryId: string): string {
  return `ext:${packId}:${entryId}`;
}

export function paletteEntriesFromPacks(packs: PackCatalogSummary[]): PaletteEntry[] {
  const entries: PaletteEntry[] = [];
  for (const pack of packs) {
    const packId = (pack.id || pack.Id || "").trim();
    if (!packId) continue;
    const packName = (pack.name || pack.Name || packId).trim() || packId;
    const widgets = pack.widgets || pack.Widgets || [];
    for (const widget of widgets) {
      const widgetId = (widget.id || widget.Id || "").trim();
      if (!widgetId) continue;
      const label = (widget.name || widget.Name || widgetId).trim() || widgetId;
      entries.push({
        type: extType(packId, widgetId),
        label,
        category: `Extension · ${packName}`,
        kind: "widget",
        keywords: `${packId} ${widgetId} extension pack`
      });
    }
  }
  return entries;
}

/**
 * Appends pack palette entries after builtins. Existing types are replaced
 * with the pack catalog version (keeps search/label fresh after reload).
 */
export function mergePaletteWithPackEntries(
  base: PaletteEntry[],
  packEntries: PaletteEntry[]
): PaletteEntry[] {
  const packTypes = new Set(packEntries.map((e) => e.type));
  const withoutPack = base.filter((e) => !packTypes.has(e.type));
  return withoutPack.concat(packEntries);
}
