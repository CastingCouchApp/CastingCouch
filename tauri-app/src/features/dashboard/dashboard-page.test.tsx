import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import type { NowPlaying, ServiceStatus } from "../../lib/api";

const invokeMock = vi.fn();
const statusListeners: Array<(status: ServiceStatus) => void> = [];
const sceneListeners: Array<(scene: string) => void> = [];
const nowPlayingListeners: Array<(playing: NowPlaying) => void> = [];

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
    listenObsScene: async (onScene: (scene: string) => void) => {
      sceneListeners.push(onScene);
      return () => {
        const index = sceneListeners.indexOf(onScene);
        if (index >= 0) sceneListeners.splice(index, 1);
      };
    },
    listenNowPlaying: async (onPlaying: (playing: NowPlaying) => void) => {
      nowPlayingListeners.push(onPlaying);
      return () => {
        const index = nowPlayingListeners.indexOf(onPlaying);
        if (index >= 0) nowPlayingListeners.splice(index, 1);
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

function disconnectedStatuses(): ServiceStatus[] {
  return [
    { id: "obs", name: "OBS", state: "disconnected", detail: "" },
    { id: "twitch", name: "Twitch", state: "disconnected", detail: "" },
    { id: "spotify", name: "Spotify", state: "disconnected", detail: "" },
  ];
}

function serviceCard(name: string): HTMLElement {
  const heading = screen.getByText(name);
  const card = heading.closest(".rounded-xl");
  if (!card) {
    throw new Error(`Card for ${name} not found`);
  }
  return card as HTMLElement;
}

describe("Dashboard live service status", () => {
  beforeEach(() => {
    statusListeners.length = 0;
    sceneListeners.length = 0;
    nowPlayingListeners.length = 0;
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return disconnectedStatuses();
      }
      if (cmd === "obs_current_scene") {
        return null;
      }
      if (cmd === "now_playing") {
        return { title: "", artist: "", album: "", is_playing: false };
      }
      return undefined;
    });
  });

  it("renders OBS, Twitch and Spotify cards when services are empty", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return [];
      }
      return undefined;
    });

    renderDashboard();
    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(screen.getByText("OBS")).toBeInTheDocument();
    expect(screen.getByText("Twitch")).toBeInTheDocument();
    expect(screen.getByText("Spotify")).toBeInTheDocument();
    expect(screen.queryByText("Sidecar")).not.toBeInTheDocument();
    expect(screen.getAllByText("Getrennt")).toHaveLength(3);
  });

  it("shows disconnected empty states on each card", async () => {
    renderDashboard();
    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();

    const obs = serviceCard("OBS");
    expect(within(obs).getByText("Getrennt")).toBeInTheDocument();
    expect(within(obs).getByText("Keine Szene")).toBeInTheDocument();

    const twitch = serviceCard("Twitch");
    expect(within(twitch).getByText("Getrennt")).toBeInTheDocument();
    expect(within(twitch).getByText("Nicht angemeldet")).toBeInTheDocument();

    const spotify = serviceCard("Spotify");
    expect(within(spotify).getByText("Getrennt")).toBeInTheDocument();
    expect(within(spotify).getByText("Keine Wiedergabe")).toBeInTheDocument();
  });

  it("shows error detail on a failing service card", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return [
          {
            id: "obs",
            name: "OBS",
            state: "error",
            detail: "OBS-Verbindung unterbrochen",
          },
          { id: "twitch", name: "Twitch", state: "disconnected", detail: "" },
          { id: "spotify", name: "Spotify", state: "error", detail: "Token abgelaufen" },
        ] satisfies ServiceStatus[];
      }
      return undefined;
    });

    renderDashboard();
    expect(await screen.findByText("OBS-Verbindung unterbrochen")).toBeInTheDocument();
    expect(screen.getByText("Token abgelaufen")).toBeInTheDocument();

    const obs = serviceCard("OBS");
    expect(within(obs).getByText("Fehler")).toBeInTheDocument();
    expect(within(obs).getByText("OBS-Verbindung unterbrochen").className).toContain("text-red-400");

    const spotify = serviceCard("Spotify");
    expect(within(spotify).getByText("Fehler")).toBeInTheDocument();
  });

  it("does not render sidecar on the dashboard", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return [
          ...disconnectedStatuses(),
          { id: "sidecar", name: "Sidecar", state: "connected", detail: "port 18765" },
        ];
      }
      return undefined;
    });

    renderDashboard();
    expect(await screen.findByText("OBS")).toBeInTheDocument();
    expect(screen.queryByText("Sidecar")).not.toBeInTheDocument();
    expect(screen.queryByText("port 18765")).not.toBeInTheDocument();
  });

  it("updates Twitch card from service-status event without waiting for poll", async () => {
    renderDashboard();
    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    expect(await screen.findAllByText("Getrennt")).toHaveLength(3);
    await waitFor(() => expect(statusListeners.length).toBeGreaterThan(0));

    statusListeners.forEach((listener) =>
      listener({
        id: "twitch",
        name: "Twitch",
        state: "connected",
        detail: "TwitchDev (twitchdev)",
      }),
    );

    const twitch = serviceCard("Twitch");
    expect(await within(twitch).findByText("TwitchDev (twitchdev)")).toBeInTheDocument();
    expect(within(twitch).getByText("Verbunden")).toBeInTheDocument();
  });

  it("shows OBS scene from obs-scene event without polling", async () => {
    renderDashboard();
    expect(await screen.findByText("Keine Szene")).toBeInTheDocument();
    await waitFor(() => expect(statusListeners.length).toBeGreaterThan(0));
    await waitFor(() => expect(sceneListeners.length).toBeGreaterThan(0));

    statusListeners.forEach((listener) =>
      listener({
        id: "obs",
        name: "OBS",
        state: "connected",
        detail: "ws://127.0.0.1:4455",
      }),
    );
    sceneListeners.forEach((listener) => listener("Live"));

    const obs = serviceCard("OBS");
    expect(await within(obs).findByText("Live")).toBeInTheDocument();
    expect(within(obs).getByText("Verbunden")).toBeInTheDocument();
    expect(within(obs).queryByText("Keine Szene")).not.toBeInTheDocument();
  });

  it("shows current OBS scene from obs_current_scene when already connected", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return [
          { id: "obs", name: "OBS", state: "connected", detail: "ws://127.0.0.1:4455" },
          { id: "twitch", name: "Twitch", state: "disconnected", detail: "" },
          { id: "spotify", name: "Spotify", state: "disconnected", detail: "" },
        ] satisfies ServiceStatus[];
      }
      if (cmd === "obs_current_scene") {
        return "Start";
      }
      if (cmd === "now_playing") {
        return { title: "", artist: "", album: "", is_playing: false };
      }
      return undefined;
    });

    renderDashboard();
    expect(await screen.findByRole("heading", { name: "Dashboard" })).toBeInTheDocument();
    const obs = serviceCard("OBS");
    expect(await within(obs).findByText("Start")).toBeInTheDocument();
    expect(within(obs).getByText("Verbunden")).toBeInTheDocument();
  });

  it("shows Spotify now playing from now-playing event without polling", async () => {
    renderDashboard();
    expect(await screen.findByText("Keine Wiedergabe")).toBeInTheDocument();
    await waitFor(() => expect(statusListeners.length).toBeGreaterThan(0));
    await waitFor(() => expect(nowPlayingListeners.length).toBeGreaterThan(0));

    statusListeners.forEach((listener) =>
      listener({
        id: "spotify",
        name: "Spotify",
        state: "connected",
        detail: "Contract User",
      }),
    );
    nowPlayingListeners.forEach((listener) =>
      listener({
        title: "Blinding Lights",
        artist: "The Weeknd",
        album: "After Hours",
        is_playing: true,
      }),
    );

    const spotify = serviceCard("Spotify");
    expect(await within(spotify).findByText("Blinding Lights")).toBeInTheDocument();
    expect(within(spotify).getByText("The Weeknd")).toBeInTheDocument();
    expect(within(spotify).getByText("After Hours")).toBeInTheDocument();
    expect(within(spotify).getByText("Spielt")).toBeInTheDocument();
    expect(within(spotify).getByText("Verbunden")).toBeInTheDocument();
  });
});
