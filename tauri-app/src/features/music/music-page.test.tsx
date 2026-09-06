import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import {
    RouterProvider,
    createMemoryHistory,
    createRouter,
} from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import { defaultAppSettings } from "../../lib/app-settings";
const invokeMock = vi.fn();
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invokeMock(cmd, args),
}));
function renderMusic() {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    const router = createRouter({
        routeTree,
        history: createMemoryHistory({ initialEntries: ["/music"] }),
    });
    render(
        <QueryClientProvider client={client}>
            <RouterProvider router={router} />
        </QueryClientProvider>,
    );
}
describe("Native music", () => {
    beforeEach(() => {
        invokeMock.mockReset().mockImplementation(async (cmd: string) => {
            if (cmd === "get_settings") return defaultAppSettings();
            if (cmd === "now_playing")
                return {
                    title: "Contract Song",
                    artist: "Artist",
                    album: "Album",
                    is_playing: true,
                };
            if (cmd === "ytm_now_playing")
                return { connected: false, statusText: "Bridge gestoppt" };
            if (cmd === "spotify_query") return { devices: [], items: [] };
            if (cmd === "ytm_connect")
                return "http://127.0.0.1:43831/ytmusic/install";
            return null;
        });
    });
    it("uses native playback commands and removes workflow navigation", async () => {
        renderMusic();
        expect(await screen.findByText("Contract Song")).toBeInTheDocument();
        expect(
            screen.queryByRole("link", { name: "Workflow" }),
        ).not.toBeInTheDocument();
        expect(screen.queryByText(/Sidecar/)).not.toBeInTheDocument();
        fireEvent.click(
            screen.getByRole("button", { name: "Spotify pausieren" }),
        );
        await waitFor(() =>
            expect(invokeMock).toHaveBeenCalledWith("spotify_action", {
                action: { action: "pause" },
            }),
        );
    });
    it("connects the native YouTube Music bridge", async () => {
        renderMusic();
        fireEvent.click(
            await screen.findByRole("button", {
                name: "YouTube Music verbinden",
            }),
        );
        expect(
            await screen.findByRole("link", { name: "Bookmarklet einrichten" }),
        ).toHaveAttribute("href", "http://127.0.0.1:43831/ytmusic/install");
        expect(invokeMock).toHaveBeenCalledWith("ytm_connect", undefined);
    });
    it("persists provider choice without replacing unrelated settings", async () => {
        renderMusic();
        const select = await screen.findByLabelText(
            "Musikprovider für das Overlay",
        );
        await waitFor(() => expect(select).not.toBeDisabled());
        fireEvent.change(select, { target: { value: "ytmusic" } });
        await waitFor(() =>
            expect(invokeMock).toHaveBeenCalledWith(
                "save_settings",
                expect.objectContaining({
                    original: expect.any(Object),
                    settings: expect.objectContaining({
                        MusicPlayer: expect.objectContaining({
                            Source: "ytmusic",
                        }),
                    }),
                }),
            ),
        );
    });
});
