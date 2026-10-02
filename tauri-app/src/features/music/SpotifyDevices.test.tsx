import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { it, expect, vi } from "vitest";
import { defaultAppSettings, cloneSettings } from "../../lib/app-settings";
import { SpotifyDevices } from "./SpotifyDevices";
const invoke = vi.fn();
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
it("persists device preference without losing other settings and activates it", async () => {
    const settings = cloneSettings(defaultAppSettings());
    settings.Spotify = {
        ...settings.Spotify,
        PreferredDeviceId: "offline",
        UseActiveDeviceWhenPreferredUnavailable: true,
        StartPlaylistUri: "keep",
    };
    invoke.mockImplementation(async (cmd) =>
        cmd === "get_settings"
            ? settings
            : cmd === "spotify_query"
              ? {
                    devices: [
                        {
                            id: "studio",
                            name: "Studio",
                            is_active: true,
                            is_restricted: false,
                        },
                    ],
                }
              : undefined,
    );
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <SpotifyDevices />
        </QueryClientProvider>,
    );
    const user = userEvent.setup();
    const select = await screen.findByLabelText("Spotify-Standardgerät");
    await waitFor(() => expect(select).toHaveValue("offline"));
    await user.selectOptions(select, "studio");
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("save_settings", {
            original: settings,
            settings: expect.objectContaining({
                Spotify: expect.objectContaining({
                    PreferredDeviceId: "studio",
                    StartPlaylistUri: "keep",
                }),
            }),
        }),
    );
    await user.click(
        screen.getByRole("button", { name: "Standardgerät aktivieren" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("activate_spotify_device", {
            play: false,
        }),
    );
});
