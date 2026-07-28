// @vitest-environment jsdom

import { beforeEach, describe, expect, it } from "vitest";
import { gridDivisionsForCanvas, loadEditorPrefs } from "../src/editor/shell/editor-prefs";

describe("editor prefs defaults", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("defaults grid divisions to 32×18 (16:9)", () => {
    const prefs = loadEditorPrefs();
    expect(prefs.gridH).toBe(32);
    expect(prefs.gridV).toBe(18);
  });
});

describe("gridDivisionsForCanvas", () => {
  it("keeps aspect ratio for common canvas sizes", () => {
    expect(gridDivisionsForCanvas(1920, 1080)).toEqual({ gridH: 32, gridV: 18 });
    expect(gridDivisionsForCanvas(1280, 720)).toEqual({ gridH: 32, gridV: 18 });
    expect(gridDivisionsForCanvas(1080, 1920)).toEqual({ gridH: 32, gridV: 57 });
    expect(gridDivisionsForCanvas(1080, 1080)).toEqual({ gridH: 32, gridV: 32 });
  });
});
