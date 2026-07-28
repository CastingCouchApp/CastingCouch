// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import {
  createBubatzCantinaEl,
  updateBubatzCantina,
  syncMarquee
} from "../src/shared/widgets/bubatz-cantina";
import type { LayoutItem } from "../src/shared/types";

function item(props: Record<string, unknown>, size = { w: 480, h: 120 }): LayoutItem {
  return {
    id: "test",
    type: "bubatz-cantina",
    x: 0,
    y: 0,
    w: size.w,
    h: size.h,
    z: 1,
    props
  };
}

describe("bubatz-cantina ticker", () => {
  it("shows an active marquee track in ticker mode", () => {
    const el = createBubatzCantinaEl(
      item({
        mode: "ticker",
        title: "biomilchs Bubatz Cantina",
        message: "Happy Hour",
        menuLines: "Blue Milk",
        showLeaf: true
      })
    );
    document.body.appendChild(el);
    el.style.width = "320px";
    el.style.height = "80px";

    const track = el.querySelector(".ccs-bubatz-cantina-track") as HTMLElement;
    const marquee = el.querySelector(".ccs-bubatz-cantina-marquee") as HTMLElement;
    expect(el.classList.contains("ccs-bubatz-cantina-mode-ticker")).toBe(true);
    expect(el.classList.contains("ccs-bubatz-scroll")).toBe(true);
    expect(track.classList.contains("is-active")).toBe(true);
    expect(marquee.querySelectorAll(".ccs-bubatz-cantina-marquee-seg")).toHaveLength(2);
    expect(el.querySelector(".ccs-bubatz-cantina-title")?.getAttribute("style") || "").toContain(
      "display: none"
    );

    syncMarquee(el, item({ mode: "ticker", speed: 40, repeatGap: 48 }), true);
    expect(track.classList.contains("is-scrolling")).toBe(true);
    expect(track.style.getPropertyValue("--ccs-bubatz-marquee-distance")).not.toBe("");

    document.body.removeChild(el);
  });

  it("keeps track inactive when scroll is off in sign mode", () => {
    const el = createBubatzCantinaEl(
      item({
        mode: "sign",
        scroll: false,
        title: "Cantina"
      })
    );
    const track = el.querySelector(".ccs-bubatz-cantina-track") as HTMLElement;
    expect(track.classList.contains("is-active")).toBe(false);
    expect(el.classList.contains("ccs-bubatz-scroll")).toBe(false);
  });

  it("enables track when scroll is on outside ticker mode", () => {
    const el = createBubatzCantinaEl(
      item({
        mode: "sign",
        scroll: true,
        title: "Cantina",
        message: "Open"
      })
    );
    updateBubatzCantina(
      el,
      item({
        mode: "sign",
        scroll: true,
        title: "Cantina",
        message: "Open"
      })
    );
    const track = el.querySelector(".ccs-bubatz-cantina-track") as HTMLElement;
    expect(track.classList.contains("is-active")).toBe(true);
    expect(el.classList.contains("ccs-bubatz-scroll")).toBe(true);
  });
});
