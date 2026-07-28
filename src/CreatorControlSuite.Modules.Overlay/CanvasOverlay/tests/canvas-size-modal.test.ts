// @vitest-environment jsdom

import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { closeCanvasSizeModal, openCanvasSizeModal } from "../src/editor/shell/canvas-size";

const shellCss = readFileSync(
  join(dirname(fileURLToPath(import.meta.url)), "../src/editor/editor-shell.css"),
  "utf8"
);

describe("canvas size modal", () => {
  it("opens and closes the dialog", () => {
    const modal = document.createElement("dialog");
    document.body.appendChild(modal);

    openCanvasSizeModal(modal);
    expect(modal.open).toBe(true);

    closeCanvasSizeModal(modal);
    expect(modal.open).toBe(false);
  });

  it("keeps the toolbar size button wider than icon buttons", () => {
    // `.ccs-stage-toolbar button` forces width:30px — size button must override.
    expect(shellCss).toMatch(/button\.ccs-toolbar-size[\s\S]*?width:\s*auto/);
    expect(shellCss).toMatch(/button\.ccs-toolbar-size[\s\S]*?white-space:\s*nowrap/);
  });
});
