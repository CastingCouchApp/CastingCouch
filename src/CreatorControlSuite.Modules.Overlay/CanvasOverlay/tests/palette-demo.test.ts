// @vitest-environment jsdom

import { describe, expect, it, vi, afterEach } from "vitest";
import {
  applyPaletteDemoProps,
  PALETTE_DEMO_DATA,
  demoChatMessages
} from "../src/editor/shell/palette-demo";
import { createPalettePreviewController } from "../src/editor/shell/palette-preview";
import type { LayoutItem } from "../src/shared/types";

describe("palette demo data", () => {
  it("fills blank socials/image/text props with demo values", () => {
    const socials = applyPaletteDemoProps({
      id: "1",
      kind: "widget",
      type: "socials",
      x: 0,
      y: 0,
      w: 280,
      h: 72,
      z: 1,
      props: { platform: "twitch", handle: "" }
    });
    expect(socials.props.handle).toBe("creator");

    const image = applyPaletteDemoProps({
      id: "2",
      kind: "widget",
      type: "image",
      x: 0,
      y: 0,
      w: 100,
      h: 100,
      z: 1,
      props: { src: "" }
    });
    expect(String(image.props.src)).toMatch(/^data:image\/svg\+xml/);

    const text = applyPaletteDemoProps({
      id: "3",
      kind: "widget",
      type: "text",
      x: 0,
      y: 0,
      w: 100,
      h: 40,
      z: 1,
      props: { content: "Text" }
    });
    expect(text.props.content).toBe("Willkommen im Stream");
  });

  it("does not overwrite existing non-empty props", () => {
    const item = applyPaletteDemoProps({
      id: "1",
      kind: "widget",
      type: "socials",
      x: 0,
      y: 0,
      w: 280,
      h: 72,
      z: 1,
      props: { handle: "meinchannel" }
    });
    expect(item.props.handle).toBe("meinchannel");
  });

  it("provides live overlay demo snapshot", () => {
    expect((PALETTE_DEMO_DATA.stream as { isLive: boolean }).isLive).toBe(true);
    expect((PALETTE_DEMO_DATA.music as { title: string }).title).toBeTruthy();
    expect(demoChatMessages().length).toBeGreaterThan(0);
  });
});

describe("palette preview with demo hooks", () => {
  afterEach(() => {
    document.body.innerHTML = "";
    vi.useRealTimers();
  });

  it("runs prepareItem and paintContent when showing a preview", () => {
    vi.useFakeTimers();
    const prepareItem = vi.fn((item: LayoutItem) => ({
      ...item,
      props: { ...item.props, content: "Demo" }
    }));
    const paintContent = vi.fn((el: HTMLElement, item: LayoutItem) => {
      el.textContent = String(item.props.content || "");
      el.classList.add("painted");
    });
    const controller = createPalettePreviewController({
      createItem: (type, kind) =>
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
        }) as LayoutItem,
      createContent: () => {
        const el = document.createElement("div");
        el.className = "live";
        return el;
      },
      prepareItem,
      paintContent,
      delayMs: 10
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
    vi.advanceTimersByTime(10);

    expect(prepareItem).toHaveBeenCalled();
    expect(paintContent).toHaveBeenCalled();
    expect(document.querySelector(".painted")?.textContent).toBe("Demo");
    controller.dispose();
  });
});
