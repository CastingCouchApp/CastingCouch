export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface MagnetGuide {
  orientation: "v" | "h";
  position: number;
}

export interface MagnetResult {
  x: number;
  y: number;
  w: number;
  h: number;
  guides: MagnetGuide[];
}

const DEFAULT_THRESHOLD = 8;

function edges(r: Rect): { left: number; right: number; top: number; bottom: number; cx: number; cy: number } {
  return {
    left: r.x,
    right: r.x + r.w,
    top: r.y,
    bottom: r.y + r.h,
    cx: r.x + r.w / 2,
    cy: r.y + r.h / 2
  };
}

function nearestSnap(value: number, targets: number[], threshold: number): number | null {
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

/** Snap moving/resizing rect to other widget edges and centers. */
export function applyMagnetSnap(
  rect: Rect,
  others: Rect[],
  threshold: number = DEFAULT_THRESHOLD,
  mode: "move" | "resize" = "move"
): MagnetResult {
  if (!others.length) {
    return { ...rect, guides: [] };
  }

  const targetsX: number[] = [];
  const targetsY: number[] = [];
  for (const o of others) {
    const e = edges(o);
    targetsX.push(e.left, e.right, e.cx);
    targetsY.push(e.top, e.bottom, e.cy);
  }

  let { x, y, w, h } = rect;
  const guides: MagnetGuide[] = [];
  const self = edges(rect);

  if (mode === "move") {
    const leftSnap = nearestSnap(self.left, targetsX, threshold);
    const rightSnap = nearestSnap(self.right, targetsX, threshold);
    const cxSnap = nearestSnap(self.cx, targetsX, threshold);
    const optionsX: Array<{ delta: number; guide: number }> = [];
    if (leftSnap != null) optionsX.push({ delta: leftSnap - self.left, guide: leftSnap });
    if (rightSnap != null) optionsX.push({ delta: rightSnap - self.right, guide: rightSnap });
    if (cxSnap != null) optionsX.push({ delta: cxSnap - self.cx, guide: cxSnap });
    if (optionsX.length) {
      optionsX.sort((a, b) => Math.abs(a.delta) - Math.abs(b.delta));
      x += optionsX[0].delta;
      guides.push({ orientation: "v", position: optionsX[0].guide });
    }

    const topSnap = nearestSnap(self.top, targetsY, threshold);
    const bottomSnap = nearestSnap(self.bottom, targetsY, threshold);
    const cySnap = nearestSnap(self.cy, targetsY, threshold);
    const optionsY: Array<{ delta: number; guide: number }> = [];
    if (topSnap != null) optionsY.push({ delta: topSnap - self.top, guide: topSnap });
    if (bottomSnap != null) optionsY.push({ delta: bottomSnap - self.bottom, guide: bottomSnap });
    if (cySnap != null) optionsY.push({ delta: cySnap - self.cy, guide: cySnap });
    if (optionsY.length) {
      optionsY.sort((a, b) => Math.abs(a.delta) - Math.abs(b.delta));
      y += optionsY[0].delta;
      guides.push({ orientation: "h", position: optionsY[0].guide });
    }
  } else {
    const leftSnap = nearestSnap(self.left, targetsX, threshold);
    const rightSnap = nearestSnap(self.right, targetsX, threshold);
    const leftDist = leftSnap != null ? Math.abs(leftSnap - self.left) : threshold + 1;
    const rightDist = rightSnap != null ? Math.abs(rightSnap - self.right) : threshold + 1;
    if (rightSnap != null && rightDist <= leftDist) {
      w = Math.max(20, rightSnap - x);
      guides.push({ orientation: "v", position: rightSnap });
    } else if (leftSnap != null) {
      const right = x + w;
      x = leftSnap;
      w = Math.max(20, right - x);
      guides.push({ orientation: "v", position: leftSnap });
    }

    const topSnap = nearestSnap(self.top, targetsY, threshold);
    const bottomSnap = nearestSnap(self.bottom, targetsY, threshold);
    const topDist = topSnap != null ? Math.abs(topSnap - self.top) : threshold + 1;
    const bottomDist = bottomSnap != null ? Math.abs(bottomSnap - self.bottom) : threshold + 1;
    if (bottomSnap != null && bottomDist <= topDist) {
      h = Math.max(20, bottomSnap - y);
      guides.push({ orientation: "h", position: bottomSnap });
    } else if (topSnap != null) {
      const bottom = y + h;
      y = topSnap;
      h = Math.max(20, bottom - y);
      guides.push({ orientation: "h", position: topSnap });
    }
  }

  return { x, y, w, h, guides };
}
