import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, within } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { DashboardCommunity, DashboardObsPreview } from "./DashboardLivePanels";
import type { TwitchMetricsSnapshot } from "../../lib/api";
const invoke = vi.fn();
let metricsChanged: (snapshot: TwitchMetricsSnapshot) => void = () => {};
vi.mock("../../lib/api", () => ({
    tauriInvoke: (...args: unknown[]) => invoke(...args),
    FALLBACK_POLL_MS: 15000,
    listenTwitchMetrics: async (fn: typeof metricsChanged) => {
        metricsChanged = fn;
        return () => {};
    },
    listenStreamHistory: async () => () => {},
}));
const metrics: TwitchMetricsSnapshot = {
    connected: true,
    viewerCount: { value: 23, at: null, error: null },
    followers: { value: 100, at: null, error: null },
    subscriptions: { value: 8, at: null, error: null },
    chatters: { value: 4, at: null, error: null },
    title: "Talk",
    category: "Chatting",
    channelError: null,
};
beforeEach(() => {
    invoke.mockReset();
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "twitch_metrics_snapshot"
            ? metrics
            : cmd === "stream_history_snapshot"
              ? {
                    active: {
                        FollowersKnown: true,
                        FollowersGained: 3,
                        NewSubscriptions: 2,
                        ViewerSamples: [],
                    },
                }
              : {
                    url: "data:image/png;base64,eA==",
                    width: 1920,
                    height: 1080,
                },
    );
});
function wrapper(child: React.ReactNode) {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            {child}
        </QueryClientProvider>,
    );
}
it("shows the selected C# session statistic and actual channel totals, updates from events", async () => {
    wrapper(<DashboardCommunity statistic="NewFollowers" />);
    await screen.findByText("Talk · Chatting");
    expect(
        within(screen.getByLabelText("Neue Follower")).getByText("3"),
    ).toBeInTheDocument();
    expect(
        within(screen.getByLabelText("Zuschauer")).getByText("23"),
    ).toBeInTheDocument();
    await act(async () =>
        metricsChanged({
            ...metrics,
            viewerCount: { value: 31, at: null, error: null },
        }),
    );
    expect(
        await within(screen.getByLabelText("Zuschauer")).findByText("31"),
    ).toBeInTheDocument();
});
it("retains counts as stale when the service is disconnected", async () => {
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "twitch_metrics_snapshot"
            ? {
                  ...metrics,
                  connected: false,
                  followers: { value: 100, at: null, error: "Missing scope" },
              }
            : { active: null },
    );
    wrapper(<DashboardCommunity statistic="FollowerCount" />);
    const count = await screen.findByLabelText("Follower");
    expect(await within(count).findByText("100")).toBeInTheDocument();
    expect(within(count).getByText(/veraltet/)).toBeInTheDocument();
    expect(screen.getByText(/Missing scope/)).toBeInTheDocument();
});
it("keeps the dashboard usable when legacy viewer samples have an unsupported shape", async () => {
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "twitch_metrics_snapshot"
            ? metrics
            : {
                  active: {
                      FollowersKnown: true,
                      FollowersGained: 3,
                      ViewerSamples: { future: true },
                  },
              },
    );
    const first = wrapper(<DashboardCommunity statistic="NewFollowers" />);
    expect(
        await within(await screen.findByLabelText("Neue Follower")).findByText(
            "3",
        ),
    ).toBeInTheDocument();
    first.unmount();
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "twitch_metrics_snapshot"
            ? metrics
            : {
                  active: {
                      ViewerSamples: [
                          null,
                          {},
                          { ViewerCount: -2 },
                          { ViewerCount: 2 },
                          { ViewerCount: 4 },
                      ],
                  },
              },
    );
    wrapper(<DashboardCommunity statistic="ViewerCount" />);
    const graph = await screen.findByRole("img", {
        name: "Zuschauerverlauf der aktuellen Sitzung",
    });
    expect(
        graph.querySelector("polyline")?.getAttribute("points"),
    ).not.toContain("NaN");
});
it("uses actual OBS dimensions and the selected preview size", async () => {
    wrapper(<DashboardObsPreview enabled size="Groß" />);
    const image = await screen.findByAltText("Aktuelle OBS-Szene");
    expect(image).toHaveStyle({ aspectRatio: "1920/1080", maxWidth: "800px" });
    expect(invoke).toHaveBeenCalledWith("dashboard_obs_preview");
});
it("disables OBS polling while disconnected and surfaces preview errors", async () => {
    const view = wrapper(
        <DashboardObsPreview enabled={false} size="Standard" />,
    );
    expect(
        screen.getByText("OBS-Vorschau nicht verbunden"),
    ).toBeInTheDocument();
    expect(invoke).not.toHaveBeenCalled();
    invoke.mockRejectedValue(Error("Screenshot fehlgeschlagen"));
    view.unmount();
    wrapper(<DashboardObsPreview enabled size="Standard" />);
    expect(
        await screen.findByText("Screenshot fehlgeschlagen"),
    ).toBeInTheDocument();
});
