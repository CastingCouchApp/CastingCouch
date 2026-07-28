// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import type { PaletteEntry } from "../src/editor/shell/palette";
import {
  extType,
  mergePaletteWithPackEntries,
  paletteEntriesFromPacks,
  type PackCatalogSummary
} from "../src/editor/shell/pack-palette";

describe("pack palette entries", () => {
  const packs: PackCatalogSummary[] = [
    {
      id: "cool-kit",
      name: "Cool Kit",
      widgets: [
        { id: "banner", name: "Cool Banner" },
        { id: "badge", name: "Badge" }
      ]
    },
    {
      id: "denver-john",
      name: "Denver John",
      widgets: [{ id: "logo", name: "Logo" }]
    },
    {
      id: "empty",
      name: "Empty Pack",
      widgets: []
    }
  ];

  it("builds ext:packId:entry types", () => {
    expect(extType("cool-kit", "banner")).toBe("ext:cool-kit:banner");
  });

  it("maps pack widgets into palette entries under Extension · pack name", () => {
    const entries = paletteEntriesFromPacks(packs);
    expect(entries).toEqual([
      {
        type: "ext:cool-kit:banner",
        label: "Cool Banner",
        category: "Extension · Cool Kit",
        kind: "widget",
        keywords: "cool-kit banner extension pack"
      },
      {
        type: "ext:cool-kit:badge",
        label: "Badge",
        category: "Extension · Cool Kit",
        kind: "widget",
        keywords: "cool-kit badge extension pack"
      },
      {
        type: "ext:denver-john:logo",
        label: "Logo",
        category: "Extension · Denver John",
        kind: "widget",
        keywords: "denver-john logo extension pack"
      }
    ]);
  });

  it("merges pack entries after builtins without duplicating types", () => {
    const base: PaletteEntry[] = [
      { type: "chat", label: "Chat", category: "Interaktion", kind: "widget" },
      { type: "ext:cool-kit:banner", label: "Old", category: "Extension · Cool Kit", kind: "widget" }
    ];
    const merged = mergePaletteWithPackEntries(base, paletteEntriesFromPacks(packs));
    expect(merged.map((e) => e.type)).toEqual([
      "chat",
      "ext:cool-kit:banner",
      "ext:cool-kit:badge",
      "ext:denver-john:logo"
    ]);
    expect(merged.find((e) => e.type === "ext:cool-kit:banner")?.label).toBe("Cool Banner");
  });

  it("skips packs/widgets without ids", () => {
    expect(
      paletteEntriesFromPacks([
        { id: "", name: "Bad", widgets: [{ id: "x", name: "X" }] },
        { id: "ok", name: "Ok", widgets: [{ id: "", name: "Nope" }, { id: "yes", name: "Yes" }] }
      ])
    ).toEqual([
      {
        type: "ext:ok:yes",
        label: "Yes",
        category: "Extension · Ok",
        kind: "widget",
        keywords: "ok yes extension pack"
      }
    ]);
  });
});
