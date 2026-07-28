const STORAGE_KEY = "ccs-editor-prefs";

export interface EditorPrefs {
  obsPreview: boolean;
  grid: boolean;
  gridH: number;
  gridV: number;
  gridSnap: boolean;
  magnet: boolean;
}

export function clampGridDivisions(n: number): number {
  const v = Math.round(Number(n) || 0);
  return Math.min(64, Math.max(2, v));
}

/**
 * Grid divisions matching the canvas aspect ratio.
 * Base H=32 → 1920×1080 yields 32×18 (16:9).
 */
export function gridDivisionsForCanvas(
  canvasWidth: number,
  canvasHeight: number,
  baseH = 32
): { gridH: number; gridV: number } {
  const w = Math.max(1, Number(canvasWidth) || 1920);
  const h = Math.max(1, Number(canvasHeight) || 1080);
  const gridH = clampGridDivisions(baseH);
  const gridV = clampGridDivisions(Math.round(gridH * (h / w)));
  return { gridH, gridV };
}

const DEFAULT_GRID = gridDivisionsForCanvas(1920, 1080);

const DEFAULTS: EditorPrefs = {
  obsPreview: false,
  grid: true,
  gridH: DEFAULT_GRID.gridH,
  gridV: DEFAULT_GRID.gridV,
  gridSnap: true,
  magnet: true
};

export function loadEditorPrefs(): EditorPrefs {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { ...DEFAULTS };
    const parsed = JSON.parse(raw) as Partial<EditorPrefs>;
    return {
      obsPreview: !!parsed.obsPreview,
      grid: parsed.grid !== false,
      gridH: clampGridDivisions(parsed.gridH ?? DEFAULTS.gridH),
      gridV: clampGridDivisions(parsed.gridV ?? DEFAULTS.gridV),
      gridSnap: parsed.gridSnap !== false,
      magnet: parsed.magnet !== false
    };
  } catch {
    return { ...DEFAULTS };
  }
}

export function saveEditorPrefs(prefs: EditorPrefs): void {
  const next: EditorPrefs = {
    obsPreview: !!prefs.obsPreview,
    grid: !!prefs.grid,
    gridH: clampGridDivisions(prefs.gridH),
    gridV: clampGridDivisions(prefs.gridV),
    gridSnap: !!prefs.gridSnap,
    magnet: !!prefs.magnet
  };
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    /* ignore quota */
  }
}
