// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import {
  createFruppisLandadelEl,
  updateFruppisLandadel,
  FRUPPIS_LANDADEL_VARIANTS,
  FRUPPIS_LANDADEL_SIZE_PRESETS
} from "../src/shared/widgets/fruppis-landadel";
import type { LayoutItem } from "../src/shared/types";

function landadelItem(props: Record<string, unknown> = {}): LayoutItem {
  return {
    id: "fruppis-1",
    kind: "widget",
    type: "fruppis-landadel",
    x: 0,
    y: 0,
    w: 560,
    h: 200,
    z: 1,
    props: {
      variant: "gentry",
      sizePreset: "standard",
      name: "Peter Saul",
      title: "Anwalt",
      subtitle: "Cambridge · Landadel",
      tag: "ZWIELICHTIG",
      quote: "Weiße Schuhe, rote Hose, blauer Hoodie.",
      stats: "1,75 m · sportlich",
      ...props
    }
  };
}

describe("fruppis-landadel widget", () => {
  it("exposes variants and size presets", () => {
    expect(FRUPPIS_LANDADEL_VARIANTS).toContain("gentry");
    expect(FRUPPIS_LANDADEL_VARIANTS).toContain("counsel");
    expect(FRUPPIS_LANDADEL_VARIANTS).toContain("shadow");
    expect(FRUPPIS_LANDADEL_SIZE_PRESETS.standard).toMatchObject({ w: 560, h: 200 });
    expect(FRUPPIS_LANDADEL_SIZE_PRESETS.portrait).toMatchObject({ w: 360, h: 520 });
  });

  it("creates card with figure and escaped text content", () => {
    const el = createFruppisLandadelEl(
      landadelItem({
        name: "<script>alert(1)</script>",
        title: "<b>Hack</b>"
      })
    );

    expect(el.classList.contains("ccs-fruppis-landadel")).toBe(true);
    expect(el.classList.contains("ccs-fruppis-landadel-v-gentry")).toBe(true);
    expect(el.querySelector(".ccs-fruppis-landadel-figure")).not.toBeNull();
    expect(el.querySelector(".ccs-fruppis-landadel-name")?.textContent).toBe(
      "<script>alert(1)</script>"
    );
    expect(el.querySelector(".ccs-fruppis-landadel-name script")).toBeNull();
    expect(el.querySelector(".ccs-fruppis-landadel-title")?.textContent).toBe("<b>Hack</b>");
    expect(el.querySelector(".ccs-fruppis-landadel-title b")).toBeNull();
  });

  it("applies outfit colors and fonts as CSS variables", () => {
    const el = createFruppisLandadelEl(
      landadelItem({
        hoodieColor: "#2E6BB0",
        pantsColor: "#B91C3A",
        shoeColor: "#F2EEE6",
        hairColor: "#E8D5A3",
        eyeColor: "#4A90D9",
        color: "#2E6BB0",
        nameFontFamily: "Georgia, serif",
        nameSizePx: 32
      })
    );

    expect(el.style.getPropertyValue("--ccs-fl-hoodie")).toBe("#2E6BB0");
    expect(el.style.getPropertyValue("--ccs-fl-pants")).toBe("#B91C3A");
    expect(el.style.getPropertyValue("--ccs-fl-shoes")).toBe("#F2EEE6");
    expect(el.style.getPropertyValue("--ccs-fl-hair")).toBe("#E8D5A3");
    expect(el.style.getPropertyValue("--ccs-fl-eyes")).toBe("#4A90D9");
    expect(el.style.getPropertyValue("--ccs-fl-accent")).toBe("#2E6BB0");
    expect(el.style.getPropertyValue("--ccs-fl-name-font")).toBe("Georgia, serif");
    expect(el.style.getPropertyValue("--ccs-fl-name-size")).toBe("32px");
  });

  it("applies bgColor/bgOpacity and shadeIntensity CSS vars", () => {
    const el = createFruppisLandadelEl(
      landadelItem({
        bgColor: "#102040",
        bgOpacity: 0.5,
        shadeIntensity: 0.8,
        borderRadiusPx: 22,
        paddingPx: 20,
        gapPx: 10
      })
    );

    expect(el.style.getPropertyValue("--ccs-fl-bg")).toBe("rgba(16,32,64,0.5)");
    expect(el.style.getPropertyValue("--ccs-fl-shade")).toBe("0.8");
    expect(el.style.getPropertyValue("--ccs-fl-radius")).toBe("22px");
    expect(el.style.getPropertyValue("--ccs-fl-pad")).toBe("20px");
    expect(el.style.getPropertyValue("--ccs-fl-gap")).toBe("10px");
  });

  it("respects shadeIntensity of 0 without falling back", () => {
    const el = createFruppisLandadelEl(landadelItem({ shadeIntensity: 0 }));
    expect(el.style.getPropertyValue("--ccs-fl-shade")).toBe("0");
  });

  it("toggles visibility of sections and updates variant/layout", () => {
    const el = createFruppisLandadelEl(landadelItem());
    updateFruppisLandadel(
      el,
      landadelItem({
        variant: "shadow",
        layout: "figure-right",
        showFigure: false,
        showQuote: false,
        showTag: true,
        showStats: true,
        showSidePart: true,
        uppercaseName: true
      })
    );

    expect(el.classList.contains("ccs-fruppis-landadel-v-shadow")).toBe(true);
    expect(el.classList.contains("ccs-fruppis-landadel-layout-figure-right")).toBe(true);
    expect(el.classList.contains("ccs-fruppis-landadel-side-part")).toBe(true);
    expect(el.classList.contains("ccs-fruppis-landadel-uppercase")).toBe(true);
    expect((el.querySelector(".ccs-fruppis-landadel-figure") as HTMLElement).style.display).toBe(
      "none"
    );
    expect((el.querySelector(".ccs-fruppis-landadel-quote") as HTMLElement).style.display).toBe(
      "none"
    );
    expect((el.querySelector(".ccs-fruppis-landadel-tag") as HTMLElement).style.display).not.toBe(
      "none"
    );
    expect(el.querySelector(".ccs-fruppis-landadel-stats")?.textContent).toBe("1,75 m · sportlich");
  });
});
