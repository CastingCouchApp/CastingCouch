// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { applyEditorLayers } from "../src/editor/shell/obs-preview";
import type { EditorPrefs } from "../src/editor/shell/editor-prefs";

const prefs: EditorPrefs = {
  grid: true,
  gridH: 32,
  gridV: 18,
  gridSnap: true,
  magnet: true,
  obsPreview: false
};

describe("editor layers", () => {
  it("keeps the grid overlay above canvas items", () => {
    const canvas = document.createElement("div");
    canvas.className = "ccs-canvas";
    const item = document.createElement("div");
    item.className = "ccs-item";
    item.style.zIndex = "50";
    canvas.appendChild(item);

    applyEditorLayers(canvas, prefs);

    const grid = canvas.querySelector(":scope > .ccs-editor-grid") as HTMLElement;
    expect(grid).toBeTruthy();
    expect(grid.style.display).toBe("block");
    expect(Number(grid.style.zIndex)).toBeGreaterThan(Number(item.style.zIndex));
    expect(canvas.lastElementChild).toBe(grid);
  });
});
