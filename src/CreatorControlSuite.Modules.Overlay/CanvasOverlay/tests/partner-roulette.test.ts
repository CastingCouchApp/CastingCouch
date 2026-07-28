// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createPartnerRouletteEl,
  updatePartnerRoulette,
  resolvePartnerRouletteImages,
  PARTNER_ROULETTE_TRANSITIONS
} from "../src/shared/widgets/partner-roulette";
import type { LayoutItem } from "../src/shared/types";

function rouletteItem(props: Record<string, unknown> = {}): LayoutItem {
  return {
    id: "roulette-1",
    kind: "widget",
    type: "partner-roulette",
    x: 0,
    y: 0,
    w: 320,
    h: 180,
    z: 1,
    props: {
      images: ["https://cdn.example/a.png", "https://cdn.example/b.png", "https://cdn.example/c.png"],
      intervalMs: 2000,
      transition: "crossfade",
      transitionMs: 400,
      fit: "contain",
      objectPosition: "center",
      ...props
    }
  };
}

function activeSrc(el: HTMLElement): string {
  const active = el.querySelector<HTMLImageElement>(".ccs-partner-roulette-slide.is-active");
  return active?.getAttribute("src") || "";
}

describe("partner-roulette widget", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("exposes transitions and resolves image list", () => {
    expect(PARTNER_ROULETTE_TRANSITIONS).toEqual(["fade", "crossfade", "slide", "none"]);
    expect(resolvePartnerRouletteImages(rouletteItem())).toEqual([
      "https://cdn.example/a.png",
      "https://cdn.example/b.png",
      "https://cdn.example/c.png"
    ]);
    expect(
      resolvePartnerRouletteImages(
        rouletteItem({ images: [{ src: " https://x/1.png " }, { src: "" }, "https://x/2.png"] })
      )
    ).toEqual(["https://x/1.png", "https://x/2.png"]);
  });

  it("shows first image and rotates after interval", () => {
    const item = rouletteItem({ transition: "none", transitionMs: 0, intervalMs: 1000 });
    const el = createPartnerRouletteEl(item);
    document.body.appendChild(el);
    // Mimic runtime: refresh after append (create runs before DOM attach).
    updatePartnerRoulette(el, item);

    expect(activeSrc(el)).toBe("https://cdn.example/a.png");

    vi.advanceTimersByTime(1000);
    expect(activeSrc(el)).toBe("https://cdn.example/b.png");

    vi.advanceTimersByTime(1000);
    expect(activeSrc(el)).toBe("https://cdn.example/c.png");

    el.remove();
  });

  it("keeps rotation when update runs during a transition (data refresh)", () => {
    const item = rouletteItem({ transition: "fade", transitionMs: 500, intervalMs: 1000 });
    const el = createPartnerRouletteEl(item);
    document.body.appendChild(el);
    updatePartnerRoulette(el, item);

    expect(activeSrc(el)).toBe("https://cdn.example/a.png");

    vi.advanceTimersByTime(1000);
    // Mid-fade: neither slide may be active yet — refresh must not revert src.
    updatePartnerRoulette(el, item);
    vi.advanceTimersByTime(530);

    expect(activeSrc(el)).toBe("https://cdn.example/b.png");

    vi.advanceTimersByTime(1000);
    updatePartnerRoulette(el, item);
    vi.advanceTimersByTime(530);

    expect(activeSrc(el)).toBe("https://cdn.example/c.png");

    el.remove();
  });

  it("starts rotating even when create runs before DOM attach", () => {
    const item = rouletteItem({ transition: "none", transitionMs: 0, intervalMs: 800 });
    const el = createPartnerRouletteEl(item);
    // No update after append — timer from create must still fire.
    document.body.appendChild(el);

    vi.advanceTimersByTime(800);
    expect(activeSrc(el)).toBe("https://cdn.example/b.png");

    el.remove();
  });

  it("applies object-position as CSS variable", () => {
    const el = createPartnerRouletteEl(rouletteItem({ objectPosition: "top left" }));
    expect(el.style.getPropertyValue("--ccs-roulette-position")).toBe("top left");
  });
});
