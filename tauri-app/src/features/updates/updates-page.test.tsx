import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import type { AppVersionInfo, UpdateCheckResult, UpdatePackage } from "../../lib/api";
import { defaultAppSettings } from "../../lib/app-settings";

const invokeMock = vi.fn();

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
  };
});

function samplePackage(): UpdatePackage {
  return {
    product_id: "CreatorControlSuite",
    version: "8.0.0-beta2",
    channel: "Beta",
    download_uri: "https://example.test/pkg.zip",
    sha256: "DEADBEEF",
    size: 9,
    release_notes: "Phase 4.4 fixture notes",
    package_file_name: "CreatorControlSuite-8.0.0-beta2-win-x64.zip",
  };
}

function availableCheck(): UpdateCheckResult {
  return {
    update_available: true,
    current_version: "8.0.0-beta.1",
    package: samplePackage(),
    detail: "Update 8.0.0-beta2 verfügbar.",
  };
}

function renderUpdates() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/updates"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("Updates page", () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "app_version") {
        const info: AppVersionInfo = { version: "8.0.0-beta.1", channel: "Beta" };
        return info;
      }
      if (cmd === "get_settings") {
        return defaultAppSettings();
      }
      return undefined;
    });
  });

  it("shows current version and source, then displays the manifest after check", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "app_version") {
        return { version: "8.0.0-beta.1", channel: "Beta" };
      }
      if (cmd === "get_settings") {
        return defaultAppSettings();
      }
      if (cmd === "check_updates") {
        return availableCheck();
      }
      return undefined;
    });
    const user = userEvent.setup();
    renderUpdates();

    expect(await screen.findByRole("heading", { name: "Updates" })).toBeInTheDocument();
    expect(await screen.findByText(/8\.0\.0-beta\.1/)).toBeInTheDocument();
    expect(screen.getByText(/CastingCouchApp\/CastingCouch/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Installieren" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Prüfen" }));

    expect(await screen.findByText("Update 8.0.0-beta2 verfügbar.")).toBeInTheDocument();
    expect(screen.getByText("Phase 4.4 fixture notes")).toBeInTheDocument();
    expect(screen.getByText("DEADBEEF")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Installieren" })).toBeDisabled();
  });

  it("rejects a sha256 mismatch and keeps Installieren disabled", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "app_version") {
        return { version: "8.0.0-beta.1", channel: "Beta" };
      }
      if (cmd === "get_settings") {
        return defaultAppSettings();
      }
      if (cmd === "check_updates") {
        return availableCheck();
      }
      if (cmd === "download_update") {
        throw new Error("sha256 mismatch");
      }
      return undefined;
    });
    const user = userEvent.setup();
    renderUpdates();
    await user.click(await screen.findByRole("button", { name: "Prüfen" }));
    await user.click(await screen.findByRole("button", { name: "Download" }));

    expect(await screen.findByTestId("checksum-error")).toHaveTextContent(/sha256 mismatch/i);
    expect(screen.getByRole("button", { name: "Installieren" })).toBeDisabled();
  });

  it("enables Installieren only after a successful verify", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "app_version") {
        return { version: "8.0.0-beta.1", channel: "Beta" };
      }
      if (cmd === "get_settings") {
        return defaultAppSettings();
      }
      if (cmd === "check_updates") {
        return availableCheck();
      }
      if (cmd === "download_update") {
        return "Downloads/pkg.zip";
      }
      if (cmd === "apply_update") {
        return "Installation folgt in Phase 5.";
      }
      return undefined;
    });
    const user = userEvent.setup();
    renderUpdates();
    await user.click(await screen.findByRole("button", { name: "Prüfen" }));
    await user.click(await screen.findByRole("button", { name: "Download" }));

    const install = await screen.findByRole("button", { name: "Installieren" });
    await waitFor(() => expect(install).toBeEnabled());
    await user.click(install);
    expect(await screen.findByText(/Installation folgt in Phase 5/)).toBeInTheDocument();
    expect(invokeMock).toHaveBeenCalledWith("apply_update", undefined);
  });
});
