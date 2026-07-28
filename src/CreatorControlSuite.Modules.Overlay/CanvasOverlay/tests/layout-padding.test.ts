// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { applyItemBox } from "../src/shared/runtime/item-content";
import { fitSocials } from "../src/shared/runtime/core-functions";
import type { LayoutItem } from "../src/shared/types";

function item(partial: Partial<LayoutItem> = {}): LayoutItem {
  return {
    id: "i1",
    kind: "widget",
    type: "text",
    x: 10,
    y: 20,
    w: 200,
    h: 100,
    z: 1,
    props: {},
    ...partial
  };
}

describe("applyItemBox layout padding", () => {
  it("applies uniform padding on the item wrapper", () => {
    const el = document.createElement("div");
    applyItemBox(el, item({ padding: 12 }));
    expect(el.style.left).toBe("10px");
    expect(el.style.width).toBe("200px");
    expect(el.style.padding).toBe("12px");
  });

  it("clears padding when unset or zero", () => {
    const el = document.createElement("div");
    el.style.padding = "8px";
    applyItemBox(el, item({ padding: 0 }));
    expect(el.style.padding).toBe("");
    applyItemBox(el, item());
    expect(el.style.padding).toBe("");
  });
});

describe("fitSocials", () => {
  it("scales relative to default 280×72 box", () => {
    const el = document.createElement("div");
    Object.defineProperty(el, "clientWidth", { value: 280 });
    Object.defineProperty(el, "clientHeight", { value: 72 });
    fitSocials(el);
    expect(el.style.getPropertyValue("--ccs-socials-scale")).toBe("1");
  });

  it("scales down and up with the item box", () => {
    const el = document.createElement("div");
    Object.defineProperty(el, "clientWidth", { configurable: true, value: 140 });
    Object.defineProperty(el, "clientHeight", { configurable: true, value: 36 });
    fitSocials(el);
    expect(Number(el.style.getPropertyValue("--ccs-socials-scale"))).toBe(0.5);

    Object.defineProperty(el, "clientWidth", { configurable: true, value: 560 });
    Object.defineProperty(el, "clientHeight", { configurable: true, value: 144 });
    fitSocials(el);
    expect(Number(el.style.getPropertyValue("--ccs-socials-scale"))).toBe(1.4);
  });
});
