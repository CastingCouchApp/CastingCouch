// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { appendBubatzCantinaProps } from "../src/editor/props/panels/new-streamer-widgets";
import { textProp } from "../src/editor/controls/text-prop";
import { numProp } from "../src/editor/controls/num-prop";
import type { EditorContext } from "../src/editor/props/context";
import type { LayoutItem } from "../src/shared/types";
import { WIDGET_DEFAULTS } from "../src/shared/defaults/widgets";
import {
  createBubatzCantinaEl,
  updateBubatzCantina,
  BUBATZ_CANTINA_VARIANTS,
  BUBATZ_CANTINA_SIZE_PRESETS,
  BUBATZ_CANTINA_MODES
} from "../src/shared/widgets/bubatz-cantina";

function makeItem(props?: Record<string, unknown>): LayoutItem {
  const def = WIDGET_DEFAULTS["bubatz-cantina"];
  return {
    id: "bubatz-1",
    kind: "widget",
    type: "bubatz-cantina",
    x: 0,
    y: 0,
    w: def.w,
    h: def.h,
    z: 1,
    props: { ...(def.props || {}), ...(props || {}) }
  };
}

function makeCtx(item: LayoutItem): EditorContext {
  return {
    runtime: {} as EditorContext["runtime"],
    scheduleSave: () => undefined,
    liveItem: () => item,
    commitProp: (_from, apply) => {
      apply(item);
      return item;
    },
    previewProp: (_from, apply) => {
      apply(item);
    }
  };
}

describe("bubatz-cantina settings wiring", () => {
  it("exposes every default prop key in the inspector", () => {
    const item = makeItem();
    const host = document.createElement("div");
    appendBubatzCantinaProps(item, makeCtx(item), host);

    const keys = [...host.querySelectorAll("[data-prop], select, input, textarea")]
      .map((el) => {
        const data = (el as HTMLElement).dataset.prop;
        if (data) return data;
        // select/font/text/num/color bind via commit; detect by walking prop rows is brittle —
        // instead assert labels and known control counts below.
        return null;
      })
      .filter(Boolean);

    // boolProps set data-prop
    for (const key of [
      "showLeaf",
      "showStars",
      "showTitle",
      "showSubtitle",
      "showMessage",
      "showMenu",
      "showStatus",
      "scroll",
      "uppercase",
      "pulseLeaf",
      "twinkleStars",
      "hideWhenEmpty"
    ]) {
      expect(keys).toContain(key);
    }

    const labels = [...host.querySelectorAll(".ccs-prop-row-label, .ccs-num-prop-label")].map(
      (el) => el.textContent
    );
    expect(labels).toEqual(
      expect.arrayContaining([
        "Modus",
        "Titel",
        "Untertitel",
        "Nachricht",
        "Menüzeilen",
        "Status-Label",
        "Status-Wert",
        "Icon",
        "Style",
        "Größe",
        "Titel-Font",
        "Body-Font",
        "Titel px",
        "Body px",
        "Bubatz-Grün",
        "Blue Milk",
        "Cantina-Gold",
        "Text",
        "Muted",
        "Hintergrund",
        "BG Opacity",
        "Radius px",
        "Padding px",
        "Gap px",
        "Ticker-Speed",
        "Repeat-Gap",
        "Leaf anzeigen",
        "Sterne anzeigen",
        "Titel anzeigen",
        "Untertitel anzeigen",
        "Nachricht anzeigen",
        "Menü anzeigen",
        "Status anzeigen",
        "Scroll / Marquee",
        "Uppercase",
        "Leaf-Pulse",
        "Sternen-Twinkle",
        "Leer ausblenden"
      ])
    );

    const menu = host.querySelector("textarea.ccs-prop-row-control") as HTMLTextAreaElement;
    expect(menu).toBeTruthy();
    expect(menu.value).toContain("Blue Milk");
    expect(menu.value).toContain("\n");
  });

  it("keeps ticker speed editable above the generic speed max of 5", () => {
    const item = makeItem({ speed: 40 });
    const row = numProp("speed", "Ticker-Speed", item, makeCtx(item), 40, {
      min: 10,
      max: 200,
      step: 1
    });
    const slider = row.querySelector(".ccs-num-slider") as HTMLInputElement;
    expect(slider.max).toBe("200");
    expect(slider.value).toBe("40");
  });

  it("applies style and content props into the live DOM", () => {
    const item = makeItem({
      mode: "menu",
      variant: "hyperspace",
      sizePreset: "wide",
      title: "Test Cantina",
      subtitle: "Orbit",
      message: "Hidden in menu",
      menuLines: "A\nB",
      color: "#112233",
      color2: "#445566",
      color3: "#778899",
      textColor: "#abcdef",
      mutedColor: "#123456",
      bgColor: "#0a0a0a",
      bgOpacity: 0.5,
      titleFontFamily: "Georgia, serif",
      bodyFontFamily: "Arial, Helvetica, sans-serif",
      titleSizePx: 32,
      bodySizePx: 14,
      borderRadiusPx: 22,
      paddingPx: 18,
      gapPx: 8,
      showMessage: true,
      showMenu: true,
      showStatus: false
    });

    const el = createBubatzCantinaEl(item);
    updateBubatzCantina(el, item);

    expect(el.classList.contains("ccs-bubatz-cantina-mode-menu")).toBe(true);
    expect(el.classList.contains("ccs-bubatz-cantina-v-hyperspace")).toBe(true);
    expect(el.classList.contains("ccs-bubatz-cantina-s-wide")).toBe(true);
    expect(el.style.getPropertyValue("--ccs-bubatz-accent")).toBe("#112233");
    expect(el.style.getPropertyValue("--ccs-bubatz-turquoise")).toBe("#445566");
    expect(el.style.getPropertyValue("--ccs-bubatz-gold")).toBe("#778899");
    expect(el.style.getPropertyValue("--ccs-bubatz-text")).toBe("#abcdef");
    expect(el.style.getPropertyValue("--ccs-bubatz-muted")).toBe("#123456");
    expect(el.style.getPropertyValue("--ccs-bubatz-bg")).toBe("rgba(10,10,10,0.5)");
    expect(el.style.getPropertyValue("--ccs-bubatz-title-font")).toBe("Georgia, serif");
    expect(el.style.getPropertyValue("--ccs-bubatz-body-font")).toBe("Arial, Helvetica, sans-serif");
    expect(el.style.getPropertyValue("--ccs-bubatz-title-size")).toBe("32px");
    expect(el.style.getPropertyValue("--ccs-bubatz-body-size")).toBe("14px");
    expect(el.style.getPropertyValue("--ccs-bubatz-radius")).toBe("22px");
    expect(el.style.getPropertyValue("--ccs-bubatz-pad")).toBe("18px");
    expect(el.style.getPropertyValue("--ccs-bubatz-gap")).toBe("8px");
    expect(el.querySelector(".ccs-bubatz-cantina-title")?.textContent).toBe("Test Cantina");
    expect(el.querySelectorAll(".ccs-bubatz-cantina-menu li")).toHaveLength(2);
    expect((el.querySelector(".ccs-bubatz-cantina-message") as HTMLElement).style.display).toBe(
      "none"
    );
    expect((el.querySelector(".ccs-bubatz-cantina-status") as HTMLElement).style.display).toBe(
      "none"
    );
  });

  it("keeps catalog variants/sizes/modes in sync with defaults", () => {
    const props = WIDGET_DEFAULTS["bubatz-cantina"].props as Record<string, unknown>;
    expect(BUBATZ_CANTINA_MODES).toContain(props.mode);
    expect(BUBATZ_CANTINA_VARIANTS).toContain(props.variant);
    expect(Object.keys(BUBATZ_CANTINA_SIZE_PRESETS)).toContain(props.sizePreset);
    expect(props).toHaveProperty("titleFontFamily");
    expect(props).toHaveProperty("bodyFontFamily");
  });

  it("supports multiline textProp editing", () => {
    const item = makeItem({ menuLines: "one\ntwo" });
    const row = textProp("menuLines", "Menüzeilen", item, makeCtx(item), "fallback", {
      multiline: true,
      rows: 3
    });
    const area = row.querySelector("textarea") as HTMLTextAreaElement;
    expect(area.value).toBe("one\ntwo");
    area.value = "alpha\nbeta\ngamma";
    area.dispatchEvent(new Event("change"));
    expect(item.props.menuLines).toBe("alpha\nbeta\ngamma");
  });
});
