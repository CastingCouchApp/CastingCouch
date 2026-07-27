export function extUrl(packId: string, path: string): string {
  const base = `/extensions/${encodeURIComponent(packId)}`;
  const clean = (path || "").replace(/^\/+/, "");
  return clean ? `${base}/${clean}` : base;
}
