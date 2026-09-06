// @vitest-environment node
import { readFileSync } from "node:fs";
import { describe, it, expect } from "vitest";

describe("Windows installation package", () => {
  it("provides an MSI-compatible numeric version for prerelease app versions", () => {
    const config = JSON.parse(readFileSync(new URL("../../../src-tauri/tauri.conf.json", import.meta.url), "utf8"));
    const version = config.bundle.windows.wix?.version;
    expect(version).toMatch(/^\d+\.\d+\.\d+(?:\.\d+)?$/);
    expect(version.split(".").slice(0, 3).join(".")).toBe(config.version.split("-")[0]);
  });
});
