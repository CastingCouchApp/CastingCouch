import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi } from "vitest";
import { defaultAppSettings } from "../../lib/app-settings";
import { MusicAutomation } from "./MusicAutomation";
const invoke = vi.hoisted(() => vi.fn());
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: invoke,
}));
describe("alert music automation", () => {
    it("saves ducking settings with the original document and preserves unrelated fields", async () => {
        const settings = defaultAppSettings();
        Object.assign(settings.Spotify, {
            AlertMuteVolumePercent: 25,
            PreferredDeviceId: "studio",
            MuteDuringAlerts: true,
        });
        invoke.mockImplementation(async (cmd) =>
            cmd === "get_settings" ? settings : { saved: true, warnings: [] },
        );
        render(
            <QueryClientProvider
                client={
                    new QueryClient({
                        defaultOptions: { queries: { retry: false } },
                    })
                }
            >
                <MusicAutomation />
            </QueryClientProvider>,
        );
        const volume = await screen.findByLabelText(
            "Lautstärke während Alerts (%)",
        );
        const user = userEvent.setup();
        await user.clear(volume);
        await user.type(volume, "15");
        await user.click(
            screen.getByRole("button", { name: "Musikautomatik speichern" }),
        );
        await waitFor(() =>
            expect(invoke).toHaveBeenCalledWith(
                "save_settings",
                expect.objectContaining({
                    original: settings,
                    settings: expect.objectContaining({
                        Spotify: expect.objectContaining({
                            AlertMuteVolumePercent: 15,
                            PreferredDeviceId: "studio",
                        }),
                    }),
                }),
            ),
        );
    });
});
