// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import {
  filterPaletteEntries,
  groupPaletteByCategory,
  fillCategorizedPalette,
  type PaletteEntry
} from "../src/editor/shell/palette";

const sample: PaletteEntry[] = [
  { type: "chat", label: "Chat", category: "Interaktion", kind: "widget" },
  { type: "music", label: "Music Player", category: "Media", kind: "widget" },
  { type: "frame", label: "Frame", category: "Frames", kind: "shape" },
  { type: "text", label: "Text", category: "Content", kind: "widget" }
];

describe("palette categories and search", () => {
  it("groups entries by category order of first appearance", () => {
    const groups = groupPaletteByCategory(sample);
    expect(groups.map((g) => g.category)).toEqual(["Interaktion", "Media", "Frames", "Content"]);
    expect(groups[0].items.map((i) => i.type)).toEqual(["chat"]);
  });

  it("filters by label, type, category and kind", () => {
    expect(filterPaletteEntries(sample, "mus").map((e) => e.type)).toEqual(["music"]);
    expect(filterPaletteEntries(sample, "FRAME").map((e) => e.type)).toEqual(["frame"]);
    expect(filterPaletteEntries(sample, "interaktion").map((e) => e.type)).toEqual(["chat"]);
    expect(filterPaletteEntries(sample, "shape").map((e) => e.type)).toEqual(["frame"]);
    expect(filterPaletteEntries(sample, "")).toHaveLength(4);
  });

  it("renders category sections and hides empty ones while searching", () => {
    const root = document.createElement("div");
    fillCategorizedPalette(root, sample, () => undefined, "chat");
    const sections = [...root.querySelectorAll(".ccs-palette-category")];
    expect(sections).toHaveLength(1);
    expect(sections[0].querySelector("summary")?.textContent).toContain("Interaktion");
    expect(root.querySelectorAll(".ccs-palette-item")).toHaveLength(1);
  });
});
