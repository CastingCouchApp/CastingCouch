import type { MagnetGuide, MagnetResult, Rect, SnapActiveEdges } from "./magnet";

export type { SnapActiveEdges };

function clampDivisions(n: number): number {
  return Math.min(64, Math.max(2, Math.round(Number(n) || 0)));
}

/** Pixel positions of grid lines for H×V divisions of the canvas. */
export function gridLinePositions(
  canvasW: number,
  canvasH: number,
  gridH: number,
  gridV: number
): { xs: number[]; ys: number[] } {
  const h = clampDivisions(gridH);
  const v = clampDivisions(gridV);
  const xs: number[] = [];
  const ys: number[] = [];
  for (let i = 0; i <= h; i++) xs.push((canvasW / h) * i);
  for (let j = 0; j <= v; j++) ys.push((canvasH / v) * j);
  return { xs, ys };
}

function nearest(value: number, targets: number[], threshold: number): number | null {
  let best: number | null = null;
  let bestDist = threshold + 1;
  for (const t of targets) {
    const d = Math.abs(value - t);
    if (d <= threshold && d < bestDist) {
      best = t;
      bestDist = d;
    }
  }
  return best;
}

/** Which edges move for a resize handle (`nw`/`ne`/`sw`/`se`). */
export function activeEdgesFromHandle(handle: string): SnapActiveEdges {
  return {
    left: handle.includes("w"),
    right: handle.includes("e"),
    top: handle.includes("n"),
    bottom: handle.includes("s")
  };
}

/**
 * Snap moving/resizing rect to canvas grid lines (H×V divisions).
 * Threshold defaults to ~20% of cell size (min 8px) so snap feels firm on coarse grids.
 * For resize, pass `activeEdges` so only the dragged sides snap (fixed sides stay put).
 */
export function applyGridSnap(
  rect: Rect,
  canvasW: number,
  canvasH: number,
  gridH: number,
  gridV: number,
  mode: "move" | "resize" = "move",
  thresholdX?: number,
  thresholdY?: number,
  activeEdges?: SnapActiveEdges
): MagnetResult {
  const h = clampDivisions(gridH);
  const v = clampDivisions(gridV);
  const cellW = canvasW / h;
  const cellH = canvasH / v;
  const tx = thresholdX ?? Math.max(8, Math.min(cellW * 0.2, 40));
  const ty = thresholdY ?? Math.max(8, Math.min(cellH * 0.2, 40));
  const { xs, ys } = gridLinePositions(canvasW, canvasH, h, v);

  let { x, y, w, h: height } = rect;
  const guides: MagnetGuide[] = [];
  const right = x + w;
  const bottom = y + height;

  if (mode === "move") {
    const leftSnap = nearest(x, xs, tx);
    const rightSnap = nearest(right, xs, tx);
    const optionsX: Array<{ delta: number; guide: number }> = [];
    if (leftSnap != null) optionsX.push({ delta: leftSnap - x, guide: leftSnap });
    if (rightSnap != null) optionsX.push({ delta: rightSnap - right, guide: rightSnap });
    if (optionsX.length) {
      optionsX.sort((a, b) => Math.abs(a.delta) - Math.abs(b.delta));
      x += optionsX[0].delta;
      guides.push({ orientation: "v", position: optionsX[0].guide });
    }

    const topSnap = nearest(y, ys, ty);
    const bottomSnap = nearest(bottom, ys, ty);
    const optionsY: Array<{ delta: number; guide: number }> = [];
    if (topSnap != null) optionsY.push({ delta: topSnap - y, guide: topSnap });
    if (bottomSnap != null) optionsY.push({ delta: bottomSnap - bottom, guide: bottomSnap });
    if (optionsY.length) {
      optionsY.sort((a, b) => Math.abs(a.delta) - Math.abs(b.delta));
      y += optionsY[0].delta;
      guides.push({ orientation: "h", position: optionsY[0].guide });
    }
  } else {
    const edges = activeEdges || { left: true, right: true, top: true, bottom: true };

    if (edges.right) {
      const rightSnap = nearest(right, xs, tx);
      if (rightSnap != null) {
        w = Math.max(20, rightSnap - x);
        guides.push({ orientation: "v", position: rightSnap });
      }
    } else if (edges.left) {
      const leftSnap = nearest(x, xs, tx);
      if (leftSnap != null) {
        x = leftSnap;
        w = Math.max(20, right - x);
        guides.push({ orientation: "v", position: leftSnap });
      }
    }

    if (edges.bottom) {
      const bottomSnap = nearest(bottom, ys, ty);
      if (bottomSnap != null) {
        height = Math.max(20, bottomSnap - y);
        guides.push({ orientation: "h", position: bottomSnap });
      }
    } else if (edges.top) {
      const topSnap = nearest(y, ys, ty);
      if (topSnap != null) {
        y = topSnap;
        height = Math.max(20, bottom - y);
        guides.push({ orientation: "h", position: topSnap });
      }
    }
  }

  return { x, y, w, h: height, guides };
}
