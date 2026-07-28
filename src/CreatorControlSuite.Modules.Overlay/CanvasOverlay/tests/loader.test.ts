// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from "vitest";
import { absoluteUrl, loadExtensions, rewriteCssUrls } from "../src/shared/extensions/loader";

describe("loadExtensions (OBS-safe fetch inject)", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    document.head.innerHTML = "";
  });

  it("injects pack CSS and JS via fetch (no link/script src onload)", async () => {
    const bodies: Record<string, string> = {
      "/extensions": JSON.stringify({
        packs: [
          {
            id: "cool-kit",
            name: "Cool Kit",
            widgets: [
              {
                id: "banner",
                name: "Banner",
                entry: "widgets/banner/index.js",
                css: "widgets/banner/banner.css"
              }
            ],
            effects: [{ id: "sparkle", name: "Sparkle", entry: "effects/sparkle/index.js" }],
            animations: [{ id: "wobble", name: "Wobble", entry: "animations/wobble/index.js" }]
          }
        ]
      }),
      "/ext/cool-kit/widgets/banner/banner.css": ".banner{color:red}",
      "/ext/cool-kit/widgets/banner/index.js": "window.__bannerLoaded=1;",
      "/ext/cool-kit/effects/sparkle/index.js": "window.__sparkleLoaded=1;",
      "/ext/cool-kit/animations/wobble/index.js": "window.__wobbleLoaded=1;"
    };

    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const href = String(input);
        const path = href.replace(/^https?:\/\/[^/]+/i, "");
        const body = bodies[path];
        if (body == null) {
          return { ok: false, status: 404, text: async () => "", json: async () => ({}) };
        }
        return {
          ok: true,
          text: async () => body,
          json: async () => JSON.parse(body)
        };
      })
    );

    const packs = await loadExtensions();
    expect(packs).toHaveLength(1);
    expect(packs[0].id).toBe("cool-kit");

    const styles = [...document.head.querySelectorAll("style[data-ccs-ext]")];
    expect(styles.some((el) => (el.textContent || "").includes(".banner{color:red}"))).toBe(true);

    const scripts = [...document.head.querySelectorAll("script[data-ccs-ext]")];
    expect(scripts).toHaveLength(3);
    expect(scripts.every((el) => el.getAttribute("src") == null || el.getAttribute("src") === "")).toBe(true);
    expect(scripts.map((el) => el.textContent).join("\n")).toContain("__bannerLoaded");
    expect(scripts.map((el) => el.textContent).join("\n")).toContain("__sparkleLoaded");
    expect(scripts.map((el) => el.textContent).join("\n")).toContain("__wobbleLoaded");
    expect(document.head.querySelectorAll("link[rel=stylesheet]")).toHaveLength(0);
  });

  it("returns an empty list when /extensions is unavailable", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ({ ok: false })));
    await expect(loadExtensions()).resolves.toEqual([]);
  });
});

describe("rewriteCssUrls / absoluteUrl", () => {
  it("rewrites relative css urls against the stylesheet location", () => {
    expect(
      rewriteCssUrls("bg:url(../assets/a.png)", "http://127.0.0.1:8765/ext/kit/widgets/x.css")
    ).toBe("bg:url(http://127.0.0.1:8765/ext/kit/assets/a.png)");
  });

  it("leaves absolute and data urls untouched", () => {
    expect(rewriteCssUrls("a:url(/x.png)", "http://127.0.0.1:8765/ext/kit/a.css")).toBe("a:url(/x.png)");
    expect(rewriteCssUrls("a:url(data:image/png;base64,xx)", "http://127.0.0.1:8765/a.css"))
      .toBe("a:url(data:image/png;base64,xx)");
  });

  it("builds absolute urls from location when available", () => {
    expect(absoluteUrl("/extensions")).toMatch(/\/extensions$/);
  });
});
