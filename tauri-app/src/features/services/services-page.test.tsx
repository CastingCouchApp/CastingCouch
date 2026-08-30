import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

function renderServices() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/services"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

function statuses(
  twitch: ServiceStatus["state"],
  spotify: ServiceStatus["state"] = "disconnected",
  twitchDetail = "",
  spotifyDetail = "",
): ServiceStatus[] {
  return [
    { id: "obs", name: "OBS", state: "disconnected", detail: "" },
    { id: "twitch", name: "Twitch", state: twitch, detail: twitchDetail },
    { id: "spotify", name: "Spotify", state: spotify, detail: spotifyDetail },
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

describe("Services route Twitch", () => {
  beforeEach(() => {
    statusListeners.length = 0;
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return statuses("disconnected");
      }
      if (cmd === "twitch_login") {
        return { id: "twitch", name: "Twitch", state: "connecting", detail: "Code: ABCD-EFGH" };
      }
      if (cmd === "twitch_logout") {
        return { id: "twitch", name: "Twitch", state: "disconnected", detail: "" };
      }
      if (cmd === "obs_scenes") {
        return [];
      }
      return undefined;
    });
  });

  it("shows Anmelden when Twitch is disconnected", async () => {
    renderServices();
    expect(await screen.findByRole("heading", { name: "Dienste" })).toBeInTheDocument();
    await screen.findByText("Twitch");
    const twitchCard = serviceCard("Twitch");
    expect(within(twitchCard).getByRole("button", { name: "Anmelden" })).toBeInTheDocument();
    expect(within(twitchCard).queryByText("OAuth folgt in Phase 3.")).not.toBeInTheDocument();
  });

  it("shows Abmelden when Twitch is connected", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return statuses("connected", "disconnected", "TwitchDev (twitchdev)");
      }
      return undefined;
    });
    renderServices();
    await screen.findByText("TwitchDev (twitchdev)");
    const twitchCard = serviceCard("Twitch");
    expect(within(twitchCard).getByRole("button", { name: "Abmelden" })).toBeInTheDocument();
    expect(within(twitchCard).queryByRole("button", { name: "Anmelden" })).not.toBeInTheDocument();
  });

  it("invokes twitch_login on Anmelden click", async () => {
    const user = userEvent.setup();
    renderServices();
    await screen.findByText("Twitch");
    const twitchCard = serviceCard("Twitch");
    await user.click(within(twitchCard).getByRole("button", { name: "Anmelden" }));
    expect(invokeMock).toHaveBeenCalledWith("twitch_login", undefined);
  });

  it("updates Twitch card from service-status event without polling", async () => {
    renderServices();
    await screen.findByText("Twitch");
    const twitchCard = serviceCard("Twitch");
    expect(within(twitchCard).getByText("disconnected")).toBeInTheDocument();
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
    expect(within(serviceCard("Twitch")).getByRole("button", { name: "Abmelden" })).toBeInTheDocument();
  });
});

describe("Services route Spotify", () => {
  beforeEach(() => {
    statusListeners.length = 0;
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return statuses("disconnected", "disconnected");
      }
      if (cmd === "spotify_login") {
        return {
          id: "spotify",
          name: "Spotify",
          state: "connecting",
          detail: "Warte auf Spotify-Anmeldung …",
        };
      }
      if (cmd === "spotify_logout") {
        return { id: "spotify", name: "Spotify", state: "disconnected", detail: "" };
      }
      if (cmd === "obs_scenes") {
        return [];
      }
      return undefined;
    });
  });

  it("shows Anmelden when Spotify is disconnected", async () => {
    renderServices();
    await screen.findByText("Spotify");
    const spotifyCard = serviceCard("Spotify");
    expect(within(spotifyCard).getByRole("button", { name: "Anmelden" })).toBeInTheDocument();
    expect(within(spotifyCard).queryByText("OAuth folgt in Phase 3.")).not.toBeInTheDocument();
  });

  it("shows Abmelden and now-playing detail when Spotify is connected", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "service_statuses") {
        return statuses(
          "disconnected",
          "connected",
          "",
          "Contract User · Contract Song – Contract Artist, Guest Artist",
        );
      }
      return undefined;
    });
    renderServices();
    await screen.findByText("Contract User · Contract Song – Contract Artist, Guest Artist");
    const spotifyCard = serviceCard("Spotify");
    expect(within(spotifyCard).getByRole("button", { name: "Abmelden" })).toBeInTheDocument();
    expect(within(spotifyCard).queryByRole("button", { name: "Anmelden" })).not.toBeInTheDocument();
  });

  it("invokes spotify_login on Anmelden click", async () => {
    const user = userEvent.setup();
    renderServices();
    await screen.findByText("Spotify");
    const spotifyCard = serviceCard("Spotify");
    await user.click(within(spotifyCard).getByRole("button", { name: "Anmelden" }));
    expect(invokeMock).toHaveBeenCalledWith("spotify_login", undefined);
  });
});
