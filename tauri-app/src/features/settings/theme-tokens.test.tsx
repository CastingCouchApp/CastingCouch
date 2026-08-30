/// <reference types="node" />
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import { THEME_CATALOG, defaultAppSettings } from "../../lib/app-settings";

const invokeMock = vi.fn(async (cmd: string, _args?: Record<string, unknown>) => {
  if (cmd === "get_settings") {
    return defaultAppSettings();
  }
  if (cmd === "obs_has_password") {
    return false;
  }
  return undefined;
});

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
  };
});

const stylesCss = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), "../../styles.css"),
  "utf8",
);

function themeBlock(themeId: string): string {
  const marker = `html[data-theme="${themeId}"]`;
  const start = stylesCss.indexOf(marker);
  expect(start).toBeGreaterThan(-1);
  const open = stylesCss.indexOf("{", start);
  const close = stylesCss.indexOf("}", open);
  return stylesCss.slice(open, close + 1);
}

function tokenFromCss(themeId: string, token: string): string {
  const match = themeBlock(themeId).match(new RegExp(`${token}:\\s*([^;]+);`));
  return match?.[1]?.trim() ?? "";
}

describe("Theme tokens", () => {
  it("defines window and brand tokens for classic and catalog themes", () => {
    const required = ["classic", "comic-sans-extravaganza", "neon-night-market", "arctic-glass-lab"];
    for (const id of required) {
      expect(THEME_CATALOG.some((theme) => theme.id === id)).toBe(true);
      expect(tokenFromCss(id, "--color-window")).toMatch(/^#/);
      expect(tokenFromCss(id, "--color-brand")).toMatch(/^#/);
      expect(tokenFromCss(id, "--color-sidebar")).toMatch(/^#/);
      expect(tokenFromCss(id, "--color-card")).toMatch(/^#/);
    }
    expect(THEME_CATALOG).toHaveLength(13);
    for (const theme of THEME_CATALOG) {
      expect(tokenFromCss(theme.id, "--color-window")).toMatch(/^#/);
    }
  });

  it("changes data-theme immediately when selecting a theme", async () => {
    const user = userEvent.setup();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const router = createRouter({
      routeTree,
      history: createMemoryHistory({ initialEntries: ["/settings"] }),
    });
    render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    );

    expect(tokenFromCss("classic", "--color-brand")).not.toBe(
      tokenFromCss("comic-sans-extravaganza", "--color-brand"),
    );
    expect(tokenFromCss("comic-sans-extravaganza", "--color-window")).toBe("#0a1a4a");

    const theme = await screen.findByLabelText("Theme");
    await user.selectOptions(theme, "comic-sans-extravaganza");
    expect(document.documentElement.dataset.theme).toBe("comic-sans-extravaganza");
  });
});
