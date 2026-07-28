export function pickVariant(raw: unknown, list: readonly string[], fallback = "classic"): string {
  const value = String(raw || fallback).toLowerCase();
  return list.includes(value) ? value : fallback;
}

export function applyVariantClasses(
  el: HTMLElement,
  prefix: string,
  variant: string,
  variants: readonly string[]
): void {
  variants.forEach((name) => {
    el.classList.remove(prefix + name);
  });
  el.classList.add(prefix + variant);
  el.dataset.variant = variant;
}

export function applySizeClass(
  el: HTMLElement,
  prefix: string,
  size: string,
  sizes: readonly string[]
): void {
  sizes.forEach((name) => {
    el.classList.remove(prefix + name);
  });
  el.classList.add(prefix + size);
  el.dataset.sizePreset = size;
}
