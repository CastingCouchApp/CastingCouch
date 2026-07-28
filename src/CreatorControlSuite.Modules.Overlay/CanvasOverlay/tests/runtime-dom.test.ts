// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { createTextEl, updateText } from "../src/shared/runtime/core-functions";
import type { LayoutItem } from "../src/shared/types";

function textItem(content: string): LayoutItem {
  return {
    id: "text-1",
    kind: "widget",
    type: "text",
    x: 0,
    y: 0,
    w: 400,
    h: 120,
    z: 1,
    props: {
      content,
      align: "left",
      verticalAlign: "top",
      color: "#ff5500"
    }
  };
}

describe("runtime DOM smoke", () => {
  it("creates and updates text widgets without interpreting markup", () => {
    const element = createTextEl(textItem("<script>alert(1)</script>"));

    expect(element.className).toBe("ccs-text");
    expect(element.querySelector("script")).toBeNull();
    expect(element.textContent).toBe("<script>alert(1)</script>");

    updateText(element, textItem("Live"));

    expect(element.textContent).toBe("Live");
    expect(element.style.getPropertyValue("--ccs-text-color")).toBe("#ff5500");
    expect(element.style.getPropertyValue("--ccs-text-justify")).toBe("flex-start");
  });
});
