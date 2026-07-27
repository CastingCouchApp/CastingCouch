export function parseHexColor(color: unknown): { r: number; g: number; b: number } | null {
  if (!color) return null;
  let c = String(color).trim();
  if (c[0] === "#") c = c.slice(1);
  if (c.length === 3) c = c[0] + c[0] + c[1] + c[1] + c[2] + c[2];
  if (!/^[0-9a-fA-F]{6}$/.test(c)) return null;
  return {
    r: parseInt(c.slice(0, 2), 16),
    g: parseInt(c.slice(2, 4), 16),
    b: parseInt(c.slice(4, 6), 16)
  };
}

export function rgbaFrom(color: unknown, alpha: number): string {
  const rgb = parseHexColor(color);
  if (!rgb) return `rgba(255,122,0,${alpha})`;
  return `rgba(${rgb.r},${rgb.g},${rgb.b},${alpha})`;
}
