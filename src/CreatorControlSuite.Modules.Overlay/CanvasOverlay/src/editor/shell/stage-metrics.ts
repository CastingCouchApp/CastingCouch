export const EDITOR_STAGE_PADDING = 32;

export interface StageMetrics {
  scale: number;
  offsetX: number;
  offsetY: number;
  availableW: number;
  availableH: number;
}

export function getStageMetrics(
  stage: HTMLElement,
  canvasWidth: number,
  canvasHeight: number,
  padding: number = EDITOR_STAGE_PADDING
): StageMetrics {
  const cw = Math.max(1, canvasWidth || 1920);
  const ch = Math.max(1, canvasHeight || 1080);
  const availableW = Math.max(1, (stage.clientWidth || cw) - padding * 2);
  const availableH = Math.max(1, (stage.clientHeight || ch) - padding * 2);
  const scale = Math.min(availableW / cw, availableH / ch);
  return {
    scale,
    offsetX: padding + (availableW - cw * scale) / 2,
    offsetY: padding + (availableH - ch * scale) / 2,
    availableW,
    availableH
  };
}

export function clientToCanvas(
  stage: HTMLElement,
  clientX: number,
  clientY: number,
  canvasWidth: number,
  canvasHeight: number,
  padding: number = EDITOR_STAGE_PADDING
): { x: number; y: number; scale: number } {
  const rect = stage.getBoundingClientRect();
  const m = getStageMetrics(stage, canvasWidth, canvasHeight, padding);
  return {
    x: (clientX - rect.left - m.offsetX) / m.scale,
    y: (clientY - rect.top - m.offsetY) / m.scale,
    scale: m.scale
  };
}
