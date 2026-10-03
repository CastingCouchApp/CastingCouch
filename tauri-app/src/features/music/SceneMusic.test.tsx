import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import {
    cloneSettings,
    defaultAppSettings,
    type AppSettings,
} from "../../lib/app-settings";
import { SceneMusic } from "./SceneMusic";
const invoke = vi.fn();
let publishStatus:
    | ((value: import("../../lib/api").MusicAutomationStatus) => void)
    | undefined;
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenMusicAutomation: async (listener: typeof publishStatus) => {
        publishStatus = listener;
        return () => {
            publishStatus = undefined;
        };
    },
}));
let settings: AppSettings;
beforeEach(() => {
    settings = cloneSettings(defaultAppSettings());
    settings.Spotify = {
        ...settings.Spotify,
        AutomationRules: [
            {
                Id: "import",
                Name: "Intro",
                Enabled: true,
                TriggerType: "ObsSceneChanged",
                TriggerValue: "Start",
                ActionType: "StartPlaylist",
                PlaylistUri: "spotify:playlist:intro",
                Shuffle: true,
                VolumePercent: 70,
                Custom: { keep: true },
            },
            { Id: "other", TriggerType: "FutureEvent", Custom: 99 },
        ],
    } as AppSettings["Spotify"];
    Object.assign(settings, {
        Workflow: {
            AutoStartSpotifyPlaylist: false,
            AutoPlayEndMusic: true,
            PauseSpotifyOnStreamEnd: false,
        },
    });
    invoke.mockReset();
    invoke.mockImplementation(async (cmd) =>
        cmd === "get_settings"
            ? settings
            : cmd === "obs_scenes"
              ? [
                    { name: "Start", index: 0 },
                    { name: "Game", index: 1 },
                ]
              : cmd === "music_automation_status"
                ? { running: false, action: "", history: [] }
                : cmd === "save_settings"
                  ? { saved: true, warnings: [] }
                  : undefined,
    );
});
function mount() {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <SceneMusic />
        </QueryClientProvider>,
    );
}
it("edits imported rules and stream preferences without losing unknown fields or other triggers", async () => {
    mount();
    const user = userEvent.setup();
    const row = await screen.findByRole("group", { name: "Musikregel 1" });
    const volume = within(row).getByLabelText("Regellautstärke (%)");
    await user.clear(volume);
    await user.type(volume, "35");
    expect(
        screen.getByLabelText("Playlist bei Streamstart starten"),
    ).not.toBeChecked();
    expect(screen.getByLabelText("Musik in Endszene starten")).toBeChecked();
    await user.click(screen.getByLabelText("Playlist bei Streamstart starten"));
    await user.click(
        screen.getByRole("button", { name: "Szenenmusik speichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("save_settings", {
            original: settings,
            settings: expect.objectContaining({
                Spotify: expect.objectContaining({
                    StartOnStreamStart: true,
                    AutomationRules: [
                        expect.objectContaining({
                            Id: "import",
                            VolumePercent: 35,
                            Custom: { keep: true },
                        }),
                        { Id: "other", TriggerType: "FutureEvent", Custom: 99 },
                    ],
                }),
            }),
        }),
    );
});
it("tests saved scene rules and exposes fade and stop commands", async () => {
    mount();
    const user = userEvent.setup();
    await user.selectOptions(await screen.findByLabelText("Testszene"), "Game");
    await user.click(
        screen.getByRole("button", { name: "Gespeicherte Regeln testen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_automation_action", {
            action: { action: "scene", scene: "Game", force: true },
        }),
    );
    await user.click(screen.getByRole("button", { name: "Ausblenden" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_automation_action", {
            action: { action: "fade_out" },
        }),
    );
    await user.click(
        screen.getByRole("button", { name: "Musikaktion abbrechen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_automation_action", {
            action: { action: "stop" },
        }),
    );
});
it("retains failed save drafts and shows runtime failures", async () => {
    invoke.mockImplementation(async (cmd) =>
        cmd === "get_settings"
            ? settings
            : cmd === "obs_scenes"
              ? []
              : cmd === "music_automation_status"
                ? {
                      running: false,
                      history: [
                          {
                              at: "2026-10-03T00:00:00Z",
                              rule: "Broken",
                              success: false,
                              message: "Gerät nicht erreichbar",
                          },
                      ],
                  }
                : cmd === "save_settings"
                  ? Promise.reject(new Error("Konflikt"))
                  : undefined,
    );
    mount();
    const user = userEvent.setup();
    const input = await screen.findByLabelText("Startplaylist-URI");
    await user.clear(input);
    await user.type(input, "spotify:playlist:new");
    await user.click(
        screen.getByRole("button", { name: "Szenenmusik speichern" }),
    );
    expect(await screen.findByText("Error: Konflikt")).toBeInTheDocument();
    expect(input).toHaveValue("spotify:playlist:new");
    expect(screen.getByText(/Gerät nicht erreichbar/)).toBeInTheDocument();
});

it("updates status from native events and builds repeatable default rule drafts", async () => {
    mount();
    const user = userEvent.setup();
    await screen.findByLabelText("Startplaylist-URI");
    const { act } = await import("@testing-library/react");
    await act(async () =>
        publishStatus?.({ running: true, action: "Szene: Game", history: [] }),
    );
    expect(
        await screen.findByText("Musikaktion läuft: Szene: Game"),
    ).toBeInTheDocument();
    await user.click(
        screen.getByRole("button", {
            name: "Standardregeln als Entwurf erstellen",
        }),
    );
    const count = screen.getAllByRole("group").length;
    await user.click(
        screen.getByRole("button", {
            name: "Standardregeln als Entwurf erstellen",
        }),
    );
    expect(screen.getAllByRole("group")).toHaveLength(count);
    expect(
        screen.getByText("Weitere importierte Trigger bleiben erhalten."),
    ).toBeInTheDocument();
});
