// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { boolProp } from "../src/editor/controls/bool-prop";
import { featureSection } from "../src/editor/sections/prop-section";
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
  props: { show: true, animateFill: true, pulse: true }
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

describe("unified inspector checkboxes", () => {
  it("marks bool and feature toggles with ccs-check", () => {
    const bool = boolProp("show", "Zeigen", item, ctx);
    const feature = featureSection({
      id: "demo",
      title: "Feature",
      enabledKey: "animateFill",
      item,
      commit: (apply) => {
        ctx.commitProp(item, apply as (live: LayoutItem) => void);
      }
    });

    expect(bool.querySelector("input[type='checkbox']")?.classList.contains("ccs-check")).toBe(true);
    expect(feature.querySelector("input[type='checkbox']")?.classList.contains("ccs-check")).toBe(true);
  });

  it("uses Label | Checkbox order like Inner Glow / boolProp", () => {
    const bool = boolProp("showInnerGlow", "Inner Glow", item, ctx);
    const feature = featureSection({
      id: "cam-ring-pulse",
      title: "Pulse",
      enabledKey: "pulse",
      item,
      commit: (apply) => {
        ctx.commitProp(item, apply as (live: LayoutItem) => void);
      }
    });

    for (const el of [bool, feature.querySelector(".ccs-feature-header") as HTMLElement]) {
      expect(el.classList.contains("ccs-prop-row")).toBe(true);
      const kids = [...el.children];
      expect(kids[0].classList.contains("ccs-prop-row-label")).toBe(true);
      expect((kids[1] as HTMLInputElement).type).toBe("checkbox");
    }
  });
});
