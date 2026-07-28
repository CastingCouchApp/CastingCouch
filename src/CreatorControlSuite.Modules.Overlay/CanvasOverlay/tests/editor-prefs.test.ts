// @vitest-environment jsdom

import { beforeEach, describe, expect, it } from "vitest";
import { loadEditorPrefs } from "../src/editor/shell/editor-prefs";

describe("editor prefs defaults", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("defaults grid divisions to 32×16", () => {
    const prefs = loadEditorPrefs();
    expect(prefs.gridH).toBe(32);
    expect(prefs.gridV).toBe(16);
  });
});
