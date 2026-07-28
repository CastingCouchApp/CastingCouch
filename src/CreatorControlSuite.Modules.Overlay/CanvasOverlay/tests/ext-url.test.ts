import { describe, expect, it } from "vitest";
import { extUrl } from "../src/shared/extensions/ext-url";

describe("extUrl", () => {
  it("builds /ext/{packId}/… paths", () => {
    expect(extUrl("denver-john", "widgets/logo/index.js")).toBe(
      "/ext/denver-john/widgets/logo/index.js"
    );
  });

  it("strips leading slashes from relative paths", () => {
    expect(extUrl("cool-kit", "/fonts/CoolFont.woff2")).toBe(
      "/ext/cool-kit/fonts/CoolFont.woff2"
    );
  });

  it("returns pack root when path is empty", () => {
    expect(extUrl("cool-kit", "")).toBe("/ext/cool-kit");
  });
});
