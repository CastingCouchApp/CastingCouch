export interface SpacingRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface SpacingHelper {
  orientation: "v" | "h";
  /** Start coordinate along the line axis (x for v, y for h). */
  from: number;
  /** End coordinate along the line axis. */
  to: number;
  /** Perpendicular position of the measurement line. */
  cross: number;
  distance: number;
  labelX: number;
  labelY: number;
}

function overlaps1D(a0: number, a1: number, b0: number, b1: number): boolean {
  return a0 < b1 && b0 < a1;
}

function edges(r: SpacingRect) {
  return {
    left: r.x,
    right: r.x + r.w,
    top: r.y,
    bottom: r.y + r.h,
    midX: r.x + r.w / 2,
    midY: r.y + r.h / 2
  };
}

/** Compute OBS-style spacing helpers for a moving rect. */
export function computeSpacingHelpers(
  rect: SpacingRect,
  canvasW: number,
  canvasH: number,
  others: SpacingRect[] = []
): SpacingHelper[] {
  const self = edges(rect);
  const helpers: SpacingHelper[] = [];

  const pushCanvas = (
    orientation: "v" | "h",
    from: number,
    to: number,
    cross: number,
    labelX: number,
    labelY: number
  ) => {
    const distance = Math.round(Math.abs(to - from));
    if (distance <= 0) return;
    helpers.push({
      orientation,
      from: Math.min(from, to),
      to: Math.max(from, to),
      cross,
      distance,
      labelX,
      labelY
    });
  };

  // Canvas edges
  pushCanvas("h", 0, self.left, self.midY, self.left / 2, self.midY);
  pushCanvas("h", self.right, canvasW, self.midY, (self.right + canvasW) / 2, self.midY);
  pushCanvas("v", 0, self.top, self.midX, self.midX, self.top / 2);
  pushCanvas("v", self.bottom, canvasH, self.midX, self.midX, (self.bottom + canvasH) / 2);

  // Neighbor gaps (smallest positive gap per side with orthogonal overlap)
  let leftGap: { dist: number; otherRight: number } | null = null;
  let rightGap: { dist: number; otherLeft: number } | null = null;
  let topGap: { dist: number; otherBottom: number } | null = null;
  let bottomGap: { dist: number; otherTop: number } | null = null;

  for (const o of others) {
    const e = edges(o);
    if (overlaps1D(self.top, self.bottom, e.top, e.bottom)) {
      const lg = self.left - e.right;
      if (lg > 0 && (!leftGap || lg < leftGap.dist)) leftGap = { dist: lg, otherRight: e.right };
      const rg = e.left - self.right;
      if (rg > 0 && (!rightGap || rg < rightGap.dist)) rightGap = { dist: rg, otherLeft: e.left };
    }
    if (overlaps1D(self.left, self.right, e.left, e.right)) {
      const tg = self.top - e.bottom;
      if (tg > 0 && (!topGap || tg < topGap.dist)) topGap = { dist: tg, otherBottom: e.bottom };
      const bg = e.top - self.bottom;
      if (bg > 0 && (!bottomGap || bg < bottomGap.dist)) bottomGap = { dist: bg, otherTop: e.top };
    }
  }

  if (leftGap) {
    pushCanvas(
      "h",
      leftGap.otherRight,
      self.left,
      self.midY,
      (leftGap.otherRight + self.left) / 2,
      self.midY
    );
  }
  if (rightGap) {
    pushCanvas(
      "h",
      self.right,
      rightGap.otherLeft,
      self.midY,
      (self.right + rightGap.otherLeft) / 2,
      self.midY
    );
  }
  if (topGap) {
    pushCanvas(
      "v",
      topGap.otherBottom,
      self.top,
      self.midX,
      self.midX,
      (topGap.otherBottom + self.top) / 2
    );
  }
  if (bottomGap) {
    pushCanvas(
      "v",
      self.bottom,
      bottomGap.otherTop,
      self.midX,
      self.midX,
      (self.bottom + bottomGap.otherTop) / 2
    );
  }

  return helpers;
}
