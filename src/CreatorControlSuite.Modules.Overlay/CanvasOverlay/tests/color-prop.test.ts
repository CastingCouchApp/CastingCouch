// @vitest-environment jsdom

import { beforeEach, describe, expect, it } from "vitest";
import {
  COLOR_HISTORY_KEY,
  COLOR_HISTORY_MAX,
  loadColorHistory,
  pushColorHistory,
  colorProp
} from "../src/editor/controls/color-prop";
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
  props: { color: "#ff7a00" }
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

describe("color history", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("stores recent colors MRU and dedupes", () => {
    expect(pushColorHistory("#aabbcc")).toEqual(["#aabbcc"]);
    expect(pushColorHistory("#112233")).toEqual(["#112233", "#aabbcc"]);
    expect(pushColorHistory("#aabbcc")).toEqual(["#aabbcc", "#112233"]);
    expect(loadColorHistory()).toEqual(["#aabbcc", "#112233"]);
  });

  it("caps history length", () => {
    for (let i = 0; i < COLOR_HISTORY_MAX + 5; i++) {
      const hex = "#" + i.toString(16).padStart(6, "0");
      pushColorHistory(hex);
    }
    expect(loadColorHistory()).toHaveLength(COLOR_HISTORY_MAX);
    expect(localStorage.getItem(COLOR_HISTORY_KEY)).toBeTruthy();
  });

  it("renders history swatches and records commits", () => {
    pushColorHistory("#00ff00");
    const el = colorProp("color", "Farbe", item, ctx, "#ff7a00");
    const history = el.querySelector(".ccs-color-history") as HTMLElement;
    expect(history).toBeTruthy();
    expect(history.querySelectorAll(".ccs-color-swatch").length).toBe(1);

    const text = el.querySelector(".ccs-color-text") as HTMLInputElement;
    text.value = "#123456";
    text.dispatchEvent(new Event("change"));
    expect(loadColorHistory()[0]).toBe("#123456");
  });
});

describe("colorProp palette", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("places an expand handle before the picker and keeps swatches collapsed", () => {
    const el = colorProp("color", "Farbe", item, ctx, "#ff7a00");
    const row = el.querySelector(".ccs-color-prop-row") as HTMLElement;
    const kids = [...row.children];
    expect((kids[0] as HTMLElement).classList.contains("ccs-color-swatches-toggle")).toBe(true);
    expect((kids[1] as HTMLInputElement).type).toBe("color");

    const swatches = el.querySelector(".ccs-color-swatches") as HTMLElement;
    expect(swatches.hidden).toBe(true);
    (kids[0] as HTMLButtonElement).click();
    expect(swatches.hidden).toBe(false);
  });
});
