import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import type { ServiceStatus } from "../../lib/api";

const invokeMock = vi.fn();
const statusListeners: Array<(status: ServiceStatus) => void> = [];

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
    listenServiceStatus: async (onStatus: (status: ServiceStatus) => void) => {
      statusListeners.push(onStatus);
      return () => {
        const index = statusListeners.indexOf(onStatus);
        if (index >= 0) statusListeners.splice(index, 1);
      };
    },
  };
});

function renderDashboard() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("Dashboard live service status", () => {
  beforeEach(() => {
    statusListeners.length = 0;
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return [
          { id: "obs", name: "OBS", state: "disconnected", detail: "" },
          { id: "twitch", name: "Twitch", state: "disconnected", detail: "" },
          { id: "spotify", name: "Spotify", state: "disconnected", detail: "" },
        ] satisfies ServiceStatus[];
      }
      return undefined;
    });
  });

  it("updates Twitch card from service-status event without waiting for poll", async () => {
    renderDashboard();
    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(await screen.findAllByText("disconnected")).not.toHaveLength(0);
    await waitFor(() => expect(statusListeners.length).toBeGreaterThan(0));

    statusListeners.forEach((listener) =>
      listener({
        id: "twitch",
        name: "Twitch",
        state: "connected",
        detail: "TwitchDev (twitchdev)",
      }),
    );

    expect(await screen.findByText("TwitchDev (twitchdev)")).toBeInTheDocument();
    expect(screen.getByText("connected")).toBeInTheDocument();
  });
});
