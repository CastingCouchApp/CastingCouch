import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
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

function renderAbout() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/about"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("About page", () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "app_version") {
        return { version: "8.0.0-beta.1", channel: "Beta" };
      }
      if (cmd === "app_paths") {
        return "C:\\Users\\test\\AppData\\Local\\CreatorControlSuite";
      }
      if (cmd === "overlay_health_url") {
        return "http://127.0.0.1:8765/health";
      }
      if (cmd === "get_settings") {
        return defaultAppSettings();
      }
      return undefined;
    });
  });

  it("shows version, data path and overlay health link", async () => {
    renderAbout();
    expect(await screen.findByRole("heading", { name: "Über CastingCouch" })).toBeInTheDocument();
    expect(await screen.findByText("8.0.0-beta.1")).toBeInTheDocument();
    expect(screen.getByTestId("data-path")).toHaveTextContent(
      "C:\\Users\\test\\AppData\\Local\\CreatorControlSuite",
    );
    const link = screen.getByTestId("overlay-health-link");
    expect(link).toHaveAttribute("href", "http://127.0.0.1:8765/health");
    expect(link).toHaveTextContent("http://127.0.0.1:8765/health");
    expect(screen.getByText(/Loopback/)).toBeInTheDocument();
  });
});
