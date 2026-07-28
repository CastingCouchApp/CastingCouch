// @vitest-environment jsdom

import { describe, expect, it, beforeEach } from "vitest";
import { registerWidget } from "../src/shared/extensions/registry";
import { createItemContent, updateRegisteredWidget } from "../src/shared/runtime/item-content";
import type { LayoutItem } from "../src/shared/types";

describe("registered pack widgets at runtime", () => {
  beforeEach(() => {
    // registry is module-global; overwrite type each test
    registerWidget("ext:test-kit:banner", {
      defaults: { w: 200, h: 80, props: { text: "Hi" } },
      create(item) {
        const el = document.createElement("div");
        el.className = "test-banner";
        el.textContent = "empty";
        el.dataset.created = String(item.props?.text || "");
        return el;
      },
      update(el, item) {
        el.textContent = String(item.props?.text || "");
      }
    });
  });

  function item(text: string): LayoutItem {
    return {
      id: "1",
      kind: "widget",
      type: "ext:test-kit:banner",
      x: 0,
      y: 0,
      w: 200,
      h: 80,
      z: 1,
      props: { text }
    };
  }

  it("creates via registerWidget handlers", () => {
    const el = createItemContent(item("Banner"));
    expect(el.className).toBe("test-banner");
    expect(el.dataset.created).toBe("Banner");
  });

  it("updates via updateRegisteredWidget used by overlay refresh", () => {
    const el = createItemContent(item("A"));
    expect(updateRegisteredWidget(el, item("B"))).toBe(true);
    expect(el.textContent).toBe("B");
  });

  it("returns false for unknown types", () => {
    const el = document.createElement("div");
    expect(
      updateRegisteredWidget(el, { ...item("x"), type: "ext:missing:nope" })
    ).toBe(false);
  });
});
