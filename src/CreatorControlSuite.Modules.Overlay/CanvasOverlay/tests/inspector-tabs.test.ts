// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { activateInspectorTab } from "../src/editor/shell/inspector-tabs";

function makeTab(id: string): HTMLButtonElement {
  const btn = document.createElement("button");
  btn.className = "ccs-props-tab";
  btn.dataset.tab = id;
  return btn;
}

function makePane(id: string): HTMLElement {
  const pane = document.createElement("div");
  pane.className = "ccs-props-pane";
  pane.dataset.pane = id;
  return pane;
}

describe("inspector tabs", () => {
  it("shows only the active pane and marks the matching tab selected", () => {
    const tabs = [makeTab("layout"), makeTab("widget"), makeTab("effects")];
    const panes = [makePane("layout"), makePane("widget"), makePane("effects")];

    const active = activateInspectorTab(tabs, panes, "widget");

    expect(active).toBe("widget");
    expect(tabs.map((t) => t.getAttribute("aria-selected"))).toEqual([
      "false",
      "true",
      "false"
    ]);
    expect(panes.map((p) => p.hidden)).toEqual([true, false, true]);
  });

  it("falls back to layout for unknown tab ids", () => {
    const tabs = [makeTab("layout"), makeTab("widget")];
    const panes = [makePane("layout"), makePane("widget")];

    expect(activateInspectorTab(tabs, panes, "nope")).toBe("layout");
    expect(panes[0].hidden).toBe(false);
    expect(panes[1].hidden).toBe(true);
  });
});
