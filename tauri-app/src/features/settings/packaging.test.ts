// @vitest-environment node
import { readFileSync } from "node:fs";
import { describe, it, expect } from "vitest";

describe("Windows installation package", () => {
  it("keeps JavaScript and Rust Tauri packages on matching major/minor versions", () => {
    const npm = JSON.parse(readFileSync(new URL("../../../package-lock.json", import.meta.url), "utf8"));
    const cargo = readFileSync(new URL("../../../src-tauri/Cargo.lock", import.meta.url), "utf8");
    for (const [rust, javascript] of [["tauri", "@tauri-apps/api"], ["tauri-plugin-dialog", "@tauri-apps/plugin-dialog"], ["tauri-plugin-opener", "@tauri-apps/plugin-opener"]]) {
      const match = cargo.match(new RegExp(`name = "${rust}"\\r?\\nversion = "([^"]+)"`));
      expect(match, rust).toBeTruthy();
      expect(npm.packages[`node_modules/${javascript}`].version.split(".").slice(0, 2).join("."), javascript).toBe(match![1].split(".").slice(0, 2).join("."));
    }
  });
  it("provides an MSI-compatible numeric version for prerelease app versions", () => {
    const config = JSON.parse(readFileSync(new URL("../../../src-tauri/tauri.conf.json", import.meta.url), "utf8"));
    const version = config.bundle.windows.wix?.version;
    expect(version).toMatch(/^\d+\.\d+\.\d+(?:\.\d+)?$/);
    expect(version.split(".").slice(0, 3).join(".")).toBe(config.version.split("-")[0]);
  });
});
