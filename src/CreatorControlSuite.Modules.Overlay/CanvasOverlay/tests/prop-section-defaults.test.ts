// @vitest-environment jsdom

import { beforeEach, describe, expect, it } from "vitest";
import {
  advancedSection,
  contentSection,
  lookSection,
  styleSection
} from "../src/editor/sections/prop-section";
import { renderEffectsPanel } from "../src/editor/effects/effects-panel";
import { renderAnimationsPanel } from "../src/editor/animations/animations-panel";
import type { EditorContext } from "../src/editor/props/context";
import type { LayoutItem } from "../src/shared/types";

describe("widget prop section defaults (with tabs)", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it("keeps Inhalt and Look open, collapses Stil and Erweitert", () => {
    expect(contentSection("demo").root.open).toBe(true);
    expect(lookSection("demo").root.open).toBe(true);
    expect(styleSection("demo").root.open).toBe(false);
    expect(advancedSection("demo").root.open).toBe(false);
  });
});

describe("effects and animations tab panes", () => {
  const item: LayoutItem = {
    id: "i1",
    kind: "widget",
    type: "text",
    x: 0,
    y: 0,
    w: 100,
    h: 40,
    z: 1,
    props: {},
    effects: [],
    animations: []
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

  beforeEach(() => {
    sessionStorage.clear();
    (window as unknown as { CcsCanvas: Record<string, unknown> }).CcsCanvas = {
      listEffectTypes: () => ["glow"],
      listAnimationTypes: () => ["fade"]
    };
  });

  it("renders effects flat into the tab pane without a nested Effekte section", () => {
    const container = document.createElement("div");
    renderEffectsPanel(container, item, ctx);
    expect(container.querySelector(".ccs-prop-section")).toBeNull();
    expect(container.querySelector(".ccs-effects-list")).toBeTruthy();
    expect(container.textContent).toContain("Effekt hinzufügen");
  });

  it("renders animations flat into the tab pane without a nested Animationen section", () => {
    const container = document.createElement("div");
    renderAnimationsPanel(container, item, ctx);
    expect(container.querySelector(".ccs-prop-section")).toBeNull();
    expect(container.querySelector(".ccs-animations-list")).toBeTruthy();
    expect(container.textContent).toContain("Animation hinzufügen");
  });
});
