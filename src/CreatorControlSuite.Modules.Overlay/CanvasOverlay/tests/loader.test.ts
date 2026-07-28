// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from "vitest";
import { loadExtensions } from "../src/shared/extensions/loader";

describe("loadExtensions", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    document.head.innerHTML = "";
  });

  it("loads widget, effect and animation entry scripts from the catalog", async () => {
    const appendedScripts: string[] = [];
    const appendedLinks: string[] = [];
    const originalAppend = document.head.appendChild.bind(document.head);
    vi.spyOn(document.head, "appendChild").mockImplementation((node: Node) => {
      if (node instanceof HTMLScriptElement) {
        appendedScripts.push(node.src);
        queueMicrotask(() => node.onload?.(new Event("load")));
      }
      if (node instanceof HTMLLinkElement) {
        appendedLinks.push(node.href);
        queueMicrotask(() => node.onload?.(new Event("load")));
      }
      return originalAppend(node);
    });

    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        json: async () => ({
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
        })
      }))
    );

    const packs = await loadExtensions();
    expect(packs).toHaveLength(1);
    expect(packs[0].id).toBe("cool-kit");
    expect(appendedScripts.some((src) => src.includes("/ext/cool-kit/widgets/banner/index.js"))).toBe(true);
    expect(appendedScripts.some((src) => src.includes("/ext/cool-kit/effects/sparkle/index.js"))).toBe(true);
    expect(appendedScripts.some((src) => src.includes("/ext/cool-kit/animations/wobble/index.js"))).toBe(true);
    expect(appendedLinks.some((href) => href.includes("/ext/cool-kit/widgets/banner/banner.css"))).toBe(true);
  });

  it("returns an empty list when /extensions is unavailable", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => ({ ok: false })));
    await expect(loadExtensions()).resolves.toEqual([]);
  });
});
