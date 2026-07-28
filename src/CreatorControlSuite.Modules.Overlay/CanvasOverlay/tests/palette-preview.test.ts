// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from "vitest";
import {
  computePreviewScale,
  positionPalettePreview,
  mountPalettePreviewContent,
  createPalettePreviewController
} from "../src/editor/shell/palette-preview";
import { fillCategorizedPalette, type PaletteEntry } from "../src/editor/shell/palette";
import type { LayoutItem } from "../src/shared/types";

describe("palette preview helpers", () => {
  it("scales down to fit max box without upscaling", () => {
    expect(computePreviewScale(100, 50, 280, 180)).toBe(1);
    expect(computePreviewScale(560, 360, 280, 180)).toBe(0.5);
    expect(computePreviewScale(280, 400, 280, 180)).toBeCloseTo(0.45);
  });

  it("positions preview to the right of the card and clamps to viewport", () => {
    const preview = document.createElement("div");
    Object.defineProperty(preview, "offsetWidth", { value: 300 });
    Object.defineProperty(preview, "offsetHeight", { value: 200 });
    document.body.appendChild(preview);

    positionPalettePreview(preview, new DOMRect(10, 40, 120, 32), 12, { w: 800, h: 600 });
    expect(preview.style.left).toBe("142px");
    expect(preview.style.top).toBe("40px");

    positionPalettePreview(preview, new DOMRect(700, 500, 80, 30), 12, { w: 800, h: 600 });
    expect(Number.parseFloat(preview.style.left)).toBeLessThan(700);
    expect(Number.parseFloat(preview.style.top) + 200).toBeLessThanOrEqual(600);
  });

  it("mounts scaled widget content into the stage", () => {
    const stage = document.createElement("div");
    const item = {
      id: "p1",
      kind: "widget",
      type: "text",
      x: 0,
      y: 0,
      w: 400,
      h: 200,
      z: 1,
      props: { content: "Hello" }
    } as LayoutItem;
    const { scale } = mountPalettePreviewContent(stage, item, (it) => {
      const el = document.createElement("div");
      el.className = "ccs-preview-content";
      el.textContent = String(it.props?.content || "");
      return el;
    }, 200, 100);
    expect(scale).toBe(0.5);
    expect(stage.style.width).toBe("200px");
    expect(stage.style.height).toBe("100px");
    expect(stage.querySelector(".ccs-preview-content")?.textContent).toBe("Hello");
  });
});

describe("palette preview controller", () => {
  afterEach(() => {
    document.body.innerHTML = "";
    vi.useRealTimers();
  });

  it("shows a live preview after hover delay and hides on leave", () => {
    vi.useFakeTimers();
    const createItem = (type: string, kind: string): LayoutItem =>
      ({
        id: "x",
        type,
        kind,
        x: 0,
        y: 0,
        w: 120,
        h: 60,
        z: 1,
        props: {}
      }) as LayoutItem;
    const createContent = () => {
      const el = document.createElement("div");
      el.className = "live";
      el.textContent = "preview";
      return el;
    };
    const controller = createPalettePreviewController({
      createItem,
      createContent,
      delayMs: 100
    });

    const card = document.createElement("div");
    card.getBoundingClientRect = () => new DOMRect(20, 80, 140, 28);
    document.body.appendChild(card);

    controller.attach(card, {
      type: "text",
      label: "Text",
      category: "Content",
      kind: "widget"
    });

    card.dispatchEvent(new Event("pointerenter"));
    expect(document.querySelector(".ccs-palette-preview")).toBeNull();

    vi.advanceTimersByTime(100);
    const pop = document.querySelector(".ccs-palette-preview") as HTMLElement;
    expect(pop).toBeTruthy();
    expect(pop.querySelector(".ccs-palette-preview-title")?.textContent).toBe("Text");
    expect(pop.querySelector(".live")?.textContent).toBe("preview");

    card.dispatchEvent(new Event("pointerleave"));
    expect(document.querySelector(".ccs-palette-preview")).toBeNull();
    controller.dispose();
  });

  it("wires hover preview when filling the categorized palette", () => {
    vi.useFakeTimers();
    const entries: PaletteEntry[] = [
      { type: "chat", label: "Chat", category: "Interaktion", kind: "widget" }
    ];
    const root = document.createElement("div");
    document.body.appendChild(root);
    const controller = createPalettePreviewController({
      createItem: (type, kind) =>
        ({ id: "1", type, kind, x: 0, y: 0, w: 100, h: 80, z: 1, props: {} }) as LayoutItem,
      createContent: () => {
        const el = document.createElement("div");
        el.className = "wired";
        return el;
      },
      delayMs: 50
    });
    fillCategorizedPalette(root, entries, () => undefined, "", controller);
    const card = root.querySelector(".ccs-palette-item") as HTMLElement;
    card.getBoundingClientRect = () => new DOMRect(10, 10, 100, 24);
    card.dispatchEvent(new Event("pointerenter"));
    vi.advanceTimersByTime(50);
    expect(document.querySelector(".wired")).toBeTruthy();
    controller.dispose();
  });
});
