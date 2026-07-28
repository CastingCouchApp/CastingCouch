const STORAGE_KEY = "ccs-editor-prefs";

export interface EditorPrefs {
  obsPreview: boolean;
  grid: boolean;
  gridH: number;
  gridV: number;
  gridSnap: boolean;
  magnet: boolean;
}

const DEFAULTS: EditorPrefs = {
  obsPreview: false,
  grid: true,
  gridH: 32,
  gridV: 16,
  gridSnap: true,
  magnet: true
};

function clampDivisions(n: number): number {
  const v = Math.round(Number(n) || 0);
  return Math.min(64, Math.max(2, v));
}

export function loadEditorPrefs(): EditorPrefs {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { ...DEFAULTS };
    const parsed = JSON.parse(raw) as Partial<EditorPrefs>;
    return {
      obsPreview: !!parsed.obsPreview,
      grid: parsed.grid !== false,
      gridH: clampDivisions(parsed.gridH ?? DEFAULTS.gridH),
      gridV: clampDivisions(parsed.gridV ?? DEFAULTS.gridV),
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
    gridH: clampDivisions(prefs.gridH),
    gridV: clampDivisions(prefs.gridV),
    gridSnap: !!prefs.gridSnap,
    magnet: !!prefs.magnet
  };
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    /* ignore quota */
  }
}
