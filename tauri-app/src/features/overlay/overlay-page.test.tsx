import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import type { CanvasDto } from "../../lib/api";
import { defaultAppSettings } from "../../lib/app-settings";

const invokeMock = vi.fn();
const fetchMock = vi.fn();
const writeText = vi.fn().mockResolvedValue(undefined);

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
  };
});

function sampleCanvas(id: string, name: string): CanvasDto {
  return {
    id,
    name,
    editor_url: `http://127.0.0.1:8765/editor/${id}`,
    view_url: `http://127.0.0.1:8765/view/${id}`,
  };
}

let canvases: CanvasDto[];

function renderOverlay() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/overlay"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("Overlay canvas page", () => {
  beforeEach(() => {
    canvases = [sampleCanvas("default", "Canvas")];
    invokeMock.mockReset();
    fetchMock.mockReset();
    writeText.mockReset();
    writeText.mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", {
      value: { writeText },
      configurable: true,
    });
    fetchMock.mockImplementation(async () => ({
      ok: true,
      json: async () => ({ ok: true }),
    }));
    vi.stubGlobal("fetch", fetchMock);
    invokeMock.mockImplementation(async (cmd: string, args?: Record<string, unknown>) => {
      if (cmd === "get_settings") {
        return defaultAppSettings();
      }
      if (cmd === "list_canvases") {
        return canvases;
      }
      if (cmd === "overlay_health_url") {
        return "http://127.0.0.1:8765/health";
      }
      if (cmd === "open_overlay_editor") {
        return undefined;
      }
      if (cmd === "duplicate_canvas") {
        return sampleCanvas("canvas-kopie", "Canvas (Kopie)");
      }
      if (cmd === "create_canvas") {
        return sampleCanvas("neues-canvas", String(args?.name ?? "Neues Canvas"));
      }
      if (cmd === "delete_canvas") {
        return undefined;
      }
      return undefined;
    });
  });

  it("renders canvas table with view URL", async () => {
    renderOverlay();
    expect(await screen.findByRole("heading", { name: "Overlay" })).toBeInTheDocument();
    expect(screen.getByText("Canvas anlegen")).toBeInTheDocument();
    expect(await screen.findByText("Canvas")).toBeInTheDocument();
    expect(screen.getByText("http://127.0.0.1:8765/view/default")).toBeInTheDocument();
    expect(screen.getByText("View-URL")).toBeInTheDocument();
    expect(screen.getByText("Aktionen")).toBeInTheDocument();
  });

  it("shows overlay health when fetch succeeds", async () => {
    renderOverlay();
    expect(await screen.findByText("Overlay-Server: erreichbar")).toBeInTheDocument();
    expect(invokeMock).toHaveBeenCalledWith("overlay_health_url", undefined);
    expect(fetchMock).toHaveBeenCalledWith("http://127.0.0.1:8765/health");
  });

  it("opens editor via open_overlay_editor with editor URL", async () => {
    renderOverlay();
    fireEvent.click(await screen.findByRole("button", { name: "Editor öffnen" }));
    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith("open_overlay_editor", {
        id: "default",
        name: "Canvas",
        editor_url: "http://127.0.0.1:8765/editor/default",
      }),
    );
  });

  it("copies the view URL", async () => {
    renderOverlay();
    fireEvent.click(await screen.findByRole("button", { name: "URL kopieren" }));
    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith("http://127.0.0.1:8765/view/default"),
    );
  });

  it("duplicates a canvas", async () => {
    renderOverlay();
    fireEvent.click(await screen.findByRole("button", { name: "Duplizieren" }));
    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith("duplicate_canvas", { id: "default" }),
    );
  });
});
