// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { textProp } from "../src/editor/controls/text-prop";
import { selectProp } from "../src/editor/controls/select-prop";
import { boolProp } from "../src/editor/controls/bool-prop";
import { colorProp } from "../src/editor/controls/color-prop";
import { fontProp } from "../src/editor/controls/font-prop";
import { numProp } from "../src/editor/controls/num-prop";
import type { EditorContext } from "../src/editor/props/context";
import type { LayoutItem } from "../src/shared/types";

const item: LayoutItem = {
  id: "i1",
  kind: "widget",
  type: "text",
  x: 0,
  y: 0,
  w: 100,
  h: 40,
  z: 1,
  props: { content: "Hi", color: "#ff7a00", fontFamily: "Arial, sans-serif", show: true }
};

const ctx: EditorContext = {
  runtime: {} as EditorContext["runtime"],
  scheduleSave: () => undefined,
  liveItem: () => item,
  commitProp: (_from, apply) => {
    apply(item);
    return item;
  }
};

describe("inspector prop row layout", () => {
  it("uses the compact row shell for every control type", () => {
    const controls = [
      numProp("fontSizePx", "Schrift", item, ctx, 16),
      textProp("content", "Text", item, ctx, "Hi"),
      selectProp("align", "Align", item, ctx, [{ value: "left", label: "Links" }], "left"),
      boolProp("show", "Zeigen", item, ctx),
      colorProp("color", "Farbe", item, ctx, "#ff7a00"),
      fontProp("fontFamily", "Schriftart", item, ctx, "Arial, sans-serif")
    ];

    for (const el of controls) {
      expect(el.classList.contains("ccs-prop-row")).toBe(true);
      expect(el.querySelector(".ccs-prop-row-label")).toBeTruthy();
    }
  });
});
