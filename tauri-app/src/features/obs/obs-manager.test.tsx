import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    act,
    render,
    screen,
    fireEvent,
    waitFor,
} from "@testing-library/react";
import { beforeEach, it, expect, vi } from "vitest";
import { ObsManager } from "./ObsManager";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
function show(enabled = true) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    const view = render(
        <QueryClientProvider client={client}>
            <ObsManager enabled={enabled} />
        </QueryClientProvider>,
    );
    return { client, ...view };
}
beforeEach(() =>
    invoke.mockReset().mockImplementation(async (cmd: string, args: any) => {
        if (cmd === "obs_scenes") return [{ name: "Live", index: 0 }];
        if (cmd !== "obs_query") return {};
        switch (args.query.query) {
            case "profiles":
                return {
                    currentProfileName: "Main",
                    profiles: [
                        { profileName: "Main" },
                        { profileName: "Backup" },
                    ],
                };
            case "scene_collections":
                return {
                    currentSceneCollectionName: "Show",
                    sceneCollections: [{ sceneCollectionName: "Show" }],
                };
            case "transitions":
                return { transitions: [{ transitionName: "Fade" }] };
            case "current_transition":
                return {
                    transitionName: "Fade",
                    transitionDuration: 300,
                    transitionFixed: false,
                };
            case "scene_items":
                return {
                    sceneItems: [
                        {
                            sourceName: "Mic",
                            sceneItemId: 42,
                            sceneItemIndex: 0,
                            sceneItemEnabled: true,
                            sceneItemLocked: false,
                            sceneItemTransform: {
                                positionX: 12,
                                positionY: 20,
                                scaleX: 1,
                                scaleY: 1,
                                rotation: 0,
                            },
                        },
                    ],
                };
            case "inputs":
                return {
                    inputs: [
                        { inputName: "Mic", inputKind: "wasapi_input_capture" },
                    ],
                };
            case "transform":
                return {
                    sceneItemTransform: {
                        positionX: 12,
                        positionY: 20,
                        scaleX: 1,
                        scaleY: 1,
                        rotation: 0,
                    },
                };
            case "input_settings":
                return { inputSettings: {} };
            case "filters":
                return {
                    filters: [
                        { filterName: "Compressor", filterEnabled: true },
                    ],
                };
            case "mute":
                return { inputMuted: true };
            case "volume":
                return { inputVolumeDb: -12 };
            case "audio_monitor":
                return { monitorType: "OBS_MONITORING_TYPE_NONE" };
            case "audio_sync_offset":
                return { inputAudioSyncOffset: 100 };
            default:
                return {};
        }
    }),
);
it("selects an OBS profile using the native typed control", async () => {
    show();
    await screen.findByRole("option", { name: "Backup" });
    fireEvent.change(screen.getByLabelText("OBS-Profil"), {
        target: { value: "Backup" },
    });
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: { action: "set_profile", profileName: "Backup" },
        }),
    );
});
it("uses scene-item IDs and actual audio state when changing controls", async () => {
    show();
    await screen.findByRole("option", { name: "Live" });
    fireEvent.change(screen.getByLabelText("Szene verwalten"), {
        target: { value: "Live" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Mic bearbeiten" }),
    );
    const mute = await screen.findByLabelText("Stumm");
    await waitFor(() => expect(mute).toBeChecked());
    fireEvent.click(mute);
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: {
                action: "set_mute",
                inputName: "Mic",
                inputMuted: false,
            },
        }),
    );
    fireEvent.click(screen.getByLabelText("Quelle sichtbar"));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: {
                action: "set_visibility",
                sceneName: "Live",
                sceneItemId: 42,
                sceneItemEnabled: false,
            },
        }),
    );
    expect(screen.getByLabelText("Lautstärke (dB)")).toHaveValue(-12);
});
it("does not query or change OBS while disconnected", () => {
    show(false);
    expect(screen.getByText("OBS nicht verbunden")).toBeInTheDocument();
    expect(invoke).not.toHaveBeenCalled();
});
it("offers media restart/stop only for actual media sources and retries a failed command", async () => {
    const base = invoke.getMockImplementation()!;
    let attempts = 0;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "inputs")
            return {
                inputs: [{ inputName: "Mic", inputKind: "ffmpeg_source" }],
            };
        if (
            cmd === "obs_control" &&
            args.control.action === "restart_media" &&
            ++attempts === 1
        )
            throw Error("Medienquelle nicht verfügbar");
        return base(cmd, args);
    });
    show();
    await screen.findByRole("option", { name: "Live" });
    fireEvent.change(screen.getByLabelText("Szene verwalten"), {
        target: { value: "Live" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Mic bearbeiten" }),
    );
    const restart = await screen.findByRole("button", {
        name: "Medien neu starten",
    });
    expect(
        screen.queryByRole("button", { name: "Browser ohne Cache neu laden" }),
    ).not.toBeInTheDocument();
    fireEvent.click(restart);
    expect(
        await screen.findByText(/Medienquelle nicht verfügbar/),
    ).toBeInTheDocument();
    await waitFor(() => expect(restart).not.toBeDisabled());
    fireEvent.click(restart);
    expect(
        await screen.findByText("Medienneustart angefordert."),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Medien stoppen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: { action: "stop_media", inputName: "Mic" },
        }),
    );
    expect(invoke).toHaveBeenCalledWith("obs_control", {
        control: { action: "restart_media", inputName: "Mic" },
    });
});
it("refreshes an actual browser input without offering media actions", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) =>
        cmd === "obs_query" && args.query.query === "inputs"
            ? { inputs: [{ inputName: "Mic", inputKind: "browser_source" }] }
            : base(cmd, args),
    );
    show();
    await screen.findByRole("option", { name: "Mic" });
    fireEvent.change(screen.getByLabelText("Audio-/Eingangsquelle"), {
        target: { value: "Mic" },
    });
    fireEvent.click(
        await screen.findByRole("button", {
            name: "Browser ohne Cache neu laden",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: { action: "refresh_browser", inputName: "Mic" },
        }),
    );
    expect(
        screen.queryByRole("button", { name: "Medien neu starten" }),
    ).not.toBeInTheDocument();
});
it("does not use a cached source kind after input discovery fails", async () => {
    const base = invoke.getMockImplementation()!;
    let unavailable = false;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "inputs") {
            if (unavailable) throw Error("Quellenliste fehlgeschlagen");
            return {
                inputs: [{ inputName: "Mic", inputKind: "browser_source" }],
            };
        }
        return base(cmd, args);
    });
    const { client } = show();
    await screen.findByRole("option", { name: "Mic" });
    fireEvent.change(screen.getByLabelText("Audio-/Eingangsquelle"), {
        target: { value: "Mic" },
    });
    await screen.findByRole("button", { name: "Browser ohne Cache neu laden" });
    unavailable = true;
    await act(() => client.invalidateQueries({ queryKey: ["obs-management"] }));
    expect(
        await screen.findByText(/Quellenliste fehlgeschlagen/),
    ).toBeInTheDocument();
    expect(
        screen.queryByRole("button", { name: "Browser ohne Cache neu laden" }),
    ).not.toBeInTheDocument();
    expect(screen.getByText(/Quellenart unbekannt/)).toBeInTheDocument();
    expect(invoke.mock.calls.some(([cmd]) => cmd === "obs_control")).toBe(
        false,
    );
});
it("locks media controls and source selection until the actual mutation finishes", async () => {
    const base = invoke.getMockImplementation()!;
    let finish!: (value: unknown) => void;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "inputs")
            return { inputs: [{ inputName: "Mic", inputKind: "vlc_source" }] };
        if (cmd === "obs_control")
            return new Promise((resolve) => {
                finish = resolve;
            });
        return base(cmd, args);
    });
    show();
    await screen.findByRole("option", { name: "Mic" });
    fireEvent.change(screen.getByLabelText("Audio-/Eingangsquelle"), {
        target: { value: "Mic" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Medien neu starten" }),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Medien stoppen" }),
        ).toBeDisabled(),
    );
    expect(screen.getByLabelText("Audio-/Eingangsquelle")).toBeDisabled();
    expect(
        screen.queryByText("Medienneustart angefordert."),
    ).not.toBeInTheDocument();
    await act(async () => finish({}));
    expect(
        await screen.findByText("Medienneustart angefordert."),
    ).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Medien stoppen" }),
    ).not.toBeDisabled();
});

it("loads the explicit transform before submitting only the edited property", async () => {
    show();
    await screen.findByRole("option", { name: "Live" });
    fireEvent.change(screen.getByLabelText("Szene verwalten"), {
        target: { value: "Live" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Mic bearbeiten" }),
    );
    const x = await screen.findByLabelText("Position X");
    await waitFor(() => expect(x).toHaveValue(12));
    fireEvent.change(x, { target: { value: "30" } });
    fireEvent.submit(x.closest("form")!);
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: {
                action: "set_transform",
                sceneName: "Live",
                sceneItemId: 42,
                sceneItemTransform: { positionX: 30 },
            },
        }),
    );
});
