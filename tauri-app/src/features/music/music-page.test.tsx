import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import type { NowPlaying, ServiceStatus, YtmNowPlaying } from "../../lib/api";

const invokeMock = vi.fn();

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
  };
});

function renderMusic() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/music"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

function sidecarStatus(state: ServiceStatus["state"], detail = ""): ServiceStatus {
  return { id: "sidecar", name: "Sidecar", state, detail };
}

function spotifyTrack(): NowPlaying {
  return {
    title: "Contract Song",
    artist: "Contract Artist",
    album: "Contract Album",
    is_playing: true,
  };
}

function ytmTrack(): YtmNowPlaying {
  return {
    provider: "ytmusic",
    connected: true,
    isPlaying: true,
    title: "YTM Track",
    artist: "YTM Artist",
    album: "YTM Album",
    statusText: "Spielt",
  };
}

function card(name: string): HTMLElement {
  const heading = screen.getByText(name);
  const found = heading.closest(".rounded-xl");
  if (!found) {
    throw new Error(`Card for ${name} not found`);
  }
  return found as HTMLElement;
}

describe("Music page", () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "now_playing") {
        return spotifyTrack();
      }
      if (cmd === "sidecar_status") {
        return sidecarStatus("disconnected");
      }
      if (cmd === "sidecar_ytm_now_playing") {
        return ytmTrack();
      }
      return undefined;
    });
  });

  it("shows Spotify now playing and a sidecar hint when sidecar is down", async () => {
    renderMusic();
    expect(await screen.findByRole("heading", { name: "Musik" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Musik" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Workflow" })).toBeInTheDocument();

    const spotify = await waitFor(() => card("Spotify"));
    expect(within(spotify).getByText("Contract Song")).toBeInTheDocument();
    expect(within(spotify).getByText("Contract Artist")).toBeInTheDocument();
    expect(within(spotify).getByText("Spielt")).toBeInTheDocument();

    expect(screen.getByText("YouTube Music braucht den Sidecar.")).toBeInTheDocument();
    expect(screen.queryByText("YTM Track")).not.toBeInTheDocument();
    expect(invokeMock).not.toHaveBeenCalledWith("sidecar_ytm_now_playing", undefined);
  });

  it("shows the YouTube Music card when sidecar is connected", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "now_playing") {
        return spotifyTrack();
      }
      if (cmd === "sidecar_status") {
        return sidecarStatus("connected", "http://127.0.0.1:18765");
      }
      if (cmd === "sidecar_ytm_now_playing") {
        return ytmTrack();
      }
      return undefined;
    });

    renderMusic();
    expect(await screen.findByText("YTM Track")).toBeInTheDocument();
    const ytm = card("YouTube Music");
    expect(within(ytm).getByText("YTM Artist")).toBeInTheDocument();
    expect(within(ytm).getByText("Spielt")).toBeInTheDocument();
    expect(screen.queryByText("YouTube Music braucht den Sidecar.")).not.toBeInTheDocument();
    expect(invokeMock).toHaveBeenCalledWith("sidecar_ytm_now_playing", undefined);
    expect(within(card("Spotify")).getByText("Contract Song")).toBeInTheDocument();
  });
});
