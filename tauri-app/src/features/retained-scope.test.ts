import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";

describe("Retained Tauri scope", () => {
    it("does not expose workflow, sidecar, external control or licensing commands", () => {
        const host = readFileSync("src-tauri/src/lib.rs", "utf8");
        const shell = readFileSync(
            "src/components/layout/AppShell.tsx",
            "utf8",
        );
        expect(host).not.toMatch(
            /SidecarSupervisor|sidecar_workflow_run|spawn_sidecar/,
        );
        expect(shell).not.toMatch(/\/workflow|\/multi-pc|\/licensing/);
    });
});
