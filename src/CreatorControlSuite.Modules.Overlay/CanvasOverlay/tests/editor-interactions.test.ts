import { describe, expect, it } from "vitest";
import { nudgeDeltaForKey } from "../src/editor/shell/nudge";
import { cloneLayoutItem } from "../src/editor/shell/commands";
import { applyMagnetSnap } from "../src/editor/shell/magnet";
import { computeSpacingHelpers } from "../src/editor/shell/spacing";
import type { LayoutItem } from "../src/shared/types";

describe("nudgeDeltaForKey", () => {
  it("moves 1px without shift", () => {
    expect(nudgeDeltaForKey("ArrowLeft", false)).toEqual({ dx: -1, dy: 0 });
    expect(nudgeDeltaForKey("ArrowRight", false)).toEqual({ dx: 1, dy: 0 });
    expect(nudgeDeltaForKey("ArrowUp", false)).toEqual({ dx: 0, dy: -1 });
    expect(nudgeDeltaForKey("ArrowDown", false)).toEqual({ dx: 0, dy: 1 });
  });

  it("moves 10px with shift", () => {
    expect(nudgeDeltaForKey("ArrowLeft", true)).toEqual({ dx: -10, dy: 0 });
    expect(nudgeDeltaForKey("ArrowDown", true)).toEqual({ dx: 0, dy: 10 });
  });

  it("returns null for other keys", () => {
    expect(nudgeDeltaForKey("a", false)).toBeNull();
  });
});

describe("cloneLayoutItem", () => {
  it("creates a new unlocked copy with offset", () => {
    const item: LayoutItem = {
      id: "a",
      type: "image",
      kind: "widget",
      x: 10,
      y: 20,
      w: 100,
      h: 80,
      z: 1,
      locked: true,
      props: { src: "/assets/x" },
      effects: [{ type: "glow", enabled: true, settings: { intensity: 2 } }],
      animations: []
    };
    const copy = cloneLayoutItem(item, [item], 0, 0);
    expect(copy.id).not.toBe("a");
    expect(copy.x).toBe(10);
    expect(copy.y).toBe(20);
    expect(copy.locked).toBe(false);
    expect(copy.z).toBe(2);
    expect(copy.props.src).toBe("/assets/x");
    expect(copy.effects?.[0].settings).toEqual({ intensity: 2 });
    expect(copy.effects?.[0].settings).not.toBe(item.effects![0].settings);
  });
});

describe("canvas magnet snap", () => {
  it("snaps to canvas left edge without other widgets", () => {
    const result = applyMagnetSnap(
      { x: 5, y: 100, w: 200, h: 100 },
      [],
      8,
      "move",
      undefined,
      1920,
      1080
    );
    expect(result.x).toBe(0);
    expect(result.guides).toContainEqual({ orientation: "v", position: 0 });
  });

  it("snaps to canvas horizontal center", () => {
    const result = applyMagnetSnap(
      { x: 855, y: 100, w: 200, h: 100 },
      [],
      8,
      "move",
      undefined,
      1920,
      1080
    );
    // center of canvas is 960; widget center at 855+100=955 → snap cx to 960 → x=860
    expect(result.x).toBe(860);
    expect(result.guides).toContainEqual({ orientation: "v", position: 960 });
  });
});

describe("computeSpacingHelpers", () => {
  it("reports canvas edge distances", () => {
    const helpers = computeSpacingHelpers({ x: 100, y: 50, w: 200, h: 100 }, 1920, 1080, []);
    const left = helpers.find((h) => h.orientation === "h" && h.from === 0 && h.to === 100);
    const top = helpers.find((h) => h.orientation === "v" && h.from === 0 && h.to === 50);
    expect(left?.distance).toBe(100);
    expect(top?.distance).toBe(50);
  });

  it("reports neighbor gap when orthogonally overlapping", () => {
    const helpers = computeSpacingHelpers(
      { x: 200, y: 100, w: 100, h: 80 },
      1920,
      1080,
      [{ x: 50, y: 90, w: 100, h: 100 }]
    );
    const gap = helpers.find((h) => h.orientation === "h" && h.from === 150 && h.to === 200);
    expect(gap?.distance).toBe(50);
  });

  it("skips neighbor gap when overlapping", () => {
    const helpers = computeSpacingHelpers(
      { x: 100, y: 100, w: 100, h: 80 },
      1920,
      1080,
      [{ x: 50, y: 90, w: 100, h: 100 }]
    );
    const neighborGaps = helpers.filter(
      (h) => h.orientation === "h" && h.from > 0 && h.to < 1920 && h.distance < 100
    );
    // left canvas distance is 100; overlapping neighbor should not add a positive left gap
    expect(neighborGaps.every((h) => !(h.from === 150 && h.to === 100))).toBe(true);
  });
});
