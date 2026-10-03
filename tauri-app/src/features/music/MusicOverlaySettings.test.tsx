import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, it, expect, vi } from "vitest";
import { defaultAppSettings } from "../../lib/app-settings";
import { MusicOverlaySettings } from "./MusicOverlaySettings";
const invoke = vi.fn();
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
let settings = defaultAppSettings();
beforeEach(() => {
    settings = defaultAppSettings();
    invoke.mockReset().mockImplementation(async (cmd: string, args: any) => {
        if (cmd === "get_settings") return settings;
        if (cmd === "music_overlay_snapshot")
            return {
                visible: true,
                overlayEnabled: true,
                obsAudioMuted: null,
                overlayError: null,
            };
        if (cmd === "service_statuses")
            return [{ id: "obs", state: "connected" }];
        if (cmd === "obs_scenes") return [{ name: "Live" }];
        if (cmd === "obs_query")
            return args.query.query === "inputs"
                ? {
                      inputs: [
                          {
                              inputName: "Music Browser",
                              inputKind: "browser_source",
                          },
                          {
                              inputName: "Spotify",
                              inputKind: "wasapi_process_output_capture",
                          },
                      ],
                  }
                : { sceneItems: [{ sourceName: "Music Browser" }] };
        return { saved: true, warnings: ["OBS neu verbinden"] };
    });
});
function show() {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <MusicOverlaySettings />
        </QueryClientProvider>,
    );
}
it("edits the C# Spotify keys, offers OBS sources and preserves unrelated settings", async () => {
    Object.assign(settings.Spotify, {
        OverlayObsScene: "Live",
        OverlayHideWhenPaused: false,
        PreferredDeviceId: "Desktop",
        Future: { keep: 42 },
    });
    show();
    fireEvent.click(
        await screen.findByLabelText(
            "Bei Pause ausblenden (3 Sekunden Nachlauf)",
        ),
    );
    fireEvent.change(
        screen.getByLabelText("OBS-Audioquelle für Mute-Erkennung"),
        { target: { value: "Spotify Audio" } },
    );
    await waitFor(() =>
        expect(
            document.querySelector(
                'datalist[id="music-overlay-browser-sources"] option[value="Music Browser"]',
            ),
        ).not.toBeNull(),
    );
    fireEvent.click(
        screen.getByRole("button", { name: "Musik-Overlay speichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "save_settings",
            expect.objectContaining({
                original: settings,
                settings: expect.objectContaining({
                    Spotify: expect.objectContaining({
                        OverlayHideWhenPaused: true,
                        OverlayObsAudioSource: "Spotify Audio",
                        PreferredDeviceId: "Desktop",
                        Future: { keep: 42 },
                    }),
                }),
            }),
        ),
    );
    expect(await screen.findByText("OBS neu verbinden")).toBeInTheDocument();
});
it("retains a failed draft and displays actual overlay/OBS errors", async () => {
    const fallback = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd: string, args: unknown) => {
        if (cmd === "save_settings") throw Error("Speicherfehler");
        if (cmd === "music_overlay_snapshot")
            return {
                visible: false,
                overlayEnabled: true,
                obsAudioMuted: true,
                overlayError: "Quelle fehlt",
            };
        return fallback(cmd, args);
    });
    show();
    const source = await screen.findByLabelText("OBS-Overlay-Quelle");
    fireEvent.change(source, { target: { value: "Edited source" } });
    fireEvent.click(
        screen.getByRole("button", { name: "Musik-Overlay speichern" }),
    );
    expect(await screen.findByText(/Speicherfehler/)).toBeInTheDocument();
    expect(source).toHaveValue("Edited source");
    expect(screen.getByText("Quelle fehlt")).toBeInTheDocument();
    expect(screen.getByText("Musik-Overlay ausgeblendet")).toBeInTheDocument();
});
it("shows compatible defaults and explains that disabling the writer preserves existing data", async () => {
    show();
    expect(await screen.findByLabelText("Bei Mute ausblenden")).toBeChecked();
    expect(
        screen.getByLabelText(
            "Spotify-Lautstärke zur Mute-Erkennung verwenden",
        ),
    ).toBeChecked();
    expect(
        screen.getByLabelText("OBS-Audioquelle zur Mute-Erkennung verwenden"),
    ).toBeChecked();
    expect(
        screen.getByLabelText("OBS-Audioquelle für Mute-Erkennung"),
    ).toHaveValue("Spotify");
    expect(
        screen.getByText(/Vorhandene Musikdaten bleiben erhalten/),
    ).toBeInTheDocument();
});
