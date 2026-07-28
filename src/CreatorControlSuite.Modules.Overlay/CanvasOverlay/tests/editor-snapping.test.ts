import { describe, expect, it } from "vitest";
import {
  activeEdgesFromHandle,
  applyGridSnap,
  gridLinePositions
} from "../src/editor/shell/grid-snap";
import { applyMagnetSnap } from "../src/editor/shell/magnet";

describe("editor snapping", () => {
  it("creates bounded grid divisions", () => {
    const grid = gridLinePositions(1920, 1080, 1, 100);

    expect(grid.xs).toHaveLength(3);
    expect(grid.ys).toHaveLength(65);
    expect(grid.xs).toEqual([0, 960, 1920]);
  });

  it("snaps a moving item to the closest grid edge", () => {
    const result = applyGridSnap(
      { x: 953, y: 533, w: 200, h: 100 },
      1920,
      1080,
      2,
      2,
      "move",
      10,
      10
    );

    expect(result.x).toBe(960);
    expect(result.y).toBe(540);
    expect(result.guides).toEqual([
      { orientation: "v", position: 960 },
      { orientation: "h", position: 540 }
    ]);
  });

  it("keeps fixed resize edges stable", () => {
    const edges = activeEdgesFromHandle("se");
    const result = applyMagnetSnap(
      { x: 100, y: 100, w: 195, h: 195 },
      [{ x: 300, y: 300, w: 100, h: 100 }],
      8,
      "resize",
      edges
    );

    expect(result.x).toBe(100);
    expect(result.y).toBe(100);
    expect(result.w).toBe(200);
    expect(result.h).toBe(200);
  });
});
