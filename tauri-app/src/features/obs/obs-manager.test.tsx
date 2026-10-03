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
        if (cmd === "obs_current_scene") return null;
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
            case "input_catalog":
                return {
                    inputs: [
                        {
                            inputName: "Mic",
                            inputKind: "wasapi_input_capture",
                            category: "microphone",
                            inputMuted: true,
                        },
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
        if (cmd === "obs_query" && args.query.query === "input_catalog")
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
        cmd === "obs_query" && args.query.query === "input_catalog"
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
        if (cmd === "obs_query" && args.query.query === "input_catalog") {
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
    expect(screen.queryByLabelText("Stumm")).not.toBeInTheDocument();
    expect(invoke.mock.calls.some(([cmd]) => cmd === "obs_control")).toBe(
        false,
    );
});
it("locks media controls and source selection until the actual mutation finishes", async () => {
    const base = invoke.getMockImplementation()!;
    let finish!: (value: unknown) => void;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "input_catalog")
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

it("filters scenes by trimmed name, prefers the current scene, and clears hidden scene controls", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_scenes")
            return [
                { name: "Pause", index: 0 },
                { name: "Live", index: 2 },
                { name: "Intro", index: 1 },
            ];
        if (cmd === "obs_current_scene") return "lIvE";
        return base(cmd, args);
    });
    show();
    const scene = await screen.findByLabelText("Szene verwalten");
    await waitFor(() => expect(scene).toHaveValue("Live"));
    expect(
        Array.from((scene as HTMLSelectElement).options).map((o) => o.text),
    ).toEqual(["Szene auswählen", "Live", "Pause", "Intro"]);
    fireEvent.click(
        await screen.findByRole("button", { name: "Mic bearbeiten" }),
    );
    await screen.findByLabelText("Quelle sichtbar");
    fireEvent.change(screen.getByLabelText("Szenen suchen"), {
        target: { value: "  INTR  " },
    });
    expect(
        screen.queryByRole("option", { name: "Live" }),
    ).not.toBeInTheDocument();
    expect(scene).toHaveValue("");
    expect(screen.queryByLabelText("Quelle sichtbar")).not.toBeInTheDocument();
    fireEvent.change(scene, { target: { value: "Intro" } });
    fireEvent.change(screen.getByLabelText("Szenen suchen"), {
        target: { value: "" },
    });
    expect(scene).toHaveValue("Intro");
    expect(invoke.mock.calls.some(([cmd]) => cmd === "obs_control")).toBe(
        false,
    );
});

it("filters sources by name and type without changing their OBS indices and clears hidden selection", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "scene_items")
            return {
                sceneItems: [
                    {
                        sourceName: "Camera",
                        sourceType: "OBS_SOURCE_TYPE_INPUT",
                        inputKind: "dshow_input",
                        sceneItemId: 7,
                        sceneItemIndex: 2,
                        sceneItemEnabled: true,
                        sceneItemLocked: false,
                    },
                    {
                        sourceName: "Overlay",
                        sourceType: "browser_source",
                        sceneItemId: 8,
                        sceneItemIndex: 0,
                        sceneItemEnabled: true,
                        sceneItemLocked: false,
                    },
                ],
            };
        return base(cmd, args);
    });
    show();
    await screen.findByRole("option", { name: "Live" });
    fireEvent.change(screen.getByLabelText("Szene verwalten"), {
        target: { value: "Live" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Camera bearbeiten" }),
    );
    await screen.findByLabelText("Quelle sichtbar");
    fireEvent.change(screen.getByLabelText("Quellen suchen"), {
        target: { value: " BROWSER " },
    });
    expect(
        screen.queryByRole("button", { name: "Camera bearbeiten" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Quelle sichtbar")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Overlay bearbeiten" }));
    fireEvent.click(await screen.findByLabelText("Quelle sichtbar"));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: {
                action: "set_visibility",
                sceneName: "Live",
                sceneItemId: 8,
                sceneItemEnabled: false,
            },
        }),
    );
    fireEvent.change(screen.getByLabelText("Quellen suchen"), {
        target: { value: "DSHOW" },
    });
    expect(
        screen.getByRole("button", { name: "Camera bearbeiten" }),
    ).toBeInTheDocument();
    expect(
        screen.queryByRole("button", { name: "Overlay bearbeiten" }),
    ).not.toBeInTheDocument();
});

it("filters native input categories and mute state, preserves choices and reports unknown mute values", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "input_catalog")
            return {
                inputs: [
                    {
                        inputName: "Spotify",
                        inputKind: "wasapi_output_capture",
                        category: "music",
                        inputMuted: true,
                    },
                    {
                        inputName: "Alerts",
                        inputKind: "browser_source",
                        category: "browser",
                        inputMuted: false,
                    },
                    {
                        inputName: "Yeti",
                        inputKind: "wasapi_input_capture",
                        category: "microphone",
                        inputMuted: null,
                        muteError: "Mute-Abfrage abgelehnt",
                    },
                    {
                        inputName: "Game",
                        inputKind: "wasapi_output_capture",
                        category: "game",
                        inputMuted: false,
                    },
                ],
            };
        return base(cmd, args);
    });
    show();
    const choice = await screen.findByLabelText("Audio-/Eingangsquelle");
    await waitFor(() => expect(choice).toHaveValue("Alerts"));
    expect(
        Array.from((choice as HTMLSelectElement).options).map((o) => o.text),
    ).toEqual(["Quelle auswählen", "Alerts", "Game", "Yeti", "Spotify"]);
    fireEvent.change(choice, { target: { value: "Game" } });
    fireEvent.change(screen.getByLabelText("Eingänge suchen"), {
        target: { value: " WASAPI " },
    });
    expect(choice).toHaveValue("Game");
    fireEvent.change(screen.getByLabelText("Eingänge filtern"), {
        target: { value: "microphone" },
    });
    expect(choice).toHaveValue("Yeti");
    fireEvent.change(screen.getByLabelText("Eingänge filtern"), {
        target: { value: "muted" },
    });
    expect(choice).toHaveValue("Spotify");
    expect(
        screen.queryByRole("option", { name: "Yeti" }),
    ).not.toBeInTheDocument();
    expect(
        screen.getByText(/Yeti.*Mute-Abfrage abgelehnt/),
    ).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Eingänge suchen"), {
        target: { value: "nothing" },
    });
    expect(choice).toHaveValue("");
    expect(screen.queryByLabelText("Stumm")).not.toBeInTheDocument();
});

it("drops scene item controls after list failure or reused ID and recovers through refresh", async () => {
    const base = invoke.getMockImplementation()!;
    let state = "ok";
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "scene_items") {
            if (state === "error") throw Error("Szenenquellen nicht verfügbar");
            if (state === "replaced")
                return {
                    sceneItems: [
                        {
                            sourceName: "Replacement",
                            sceneItemId: 42,
                            sceneItemIndex: 0,
                            sceneItemEnabled: true,
                            sceneItemLocked: false,
                        },
                    ],
                };
        }
        return base(cmd, args);
    });
    const { client } = show();
    await screen.findByRole("option", { name: "Live" });
    fireEvent.change(screen.getByLabelText("Szene verwalten"), {
        target: { value: "Live" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Mic bearbeiten" }),
    );
    await screen.findByLabelText("Quelle sichtbar");
    state = "error";
    await act(() => client.invalidateQueries({ queryKey: ["obs-management"] }));
    await screen.findByText(/Szenenquellen nicht verfügbar/);
    expect(screen.queryByLabelText("Quelle sichtbar")).not.toBeInTheDocument();
    state = "ok";
    fireEvent.click(
        screen.getByRole("button", { name: "OBS-Verwaltung aktualisieren" }),
    );
    fireEvent.click(
        await screen.findByRole("button", { name: "Mic bearbeiten" }),
    );
    await screen.findByLabelText("Quelle sichtbar");
    state = "replaced";
    await act(() => client.invalidateQueries({ queryKey: ["obs-management"] }));
    await screen.findByRole("button", { name: "Replacement bearbeiten" });
    expect(screen.queryByLabelText("Quelle sichtbar")).not.toBeInTheDocument();
    expect(invoke.mock.calls.some(([cmd]) => cmd === "obs_control")).toBe(
        false,
    );
});

it("filters group items while using the full group count for reordering and clears selection on exit", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query" && args.query.query === "scene_items")
            return {
                sceneItems: [
                    {
                        sourceName: "Group",
                        sourceType: "group",
                        isGroup: true,
                        sceneItemId: 9,
                        sceneItemIndex: 0,
                        sceneItemEnabled: true,
                        sceneItemLocked: false,
                    },
                ],
            };
        if (cmd === "obs_query" && args.query.query === "group_items")
            return {
                sceneItems: [
                    {
                        sourceName: "Mic",
                        sourceType: "audio",
                        sceneItemId: 42,
                        sceneItemIndex: 0,
                        sceneItemEnabled: true,
                        sceneItemLocked: false,
                    },
                    {
                        sourceName: "Hidden by search",
                        sourceType: "image",
                        sceneItemId: 43,
                        sceneItemIndex: 1,
                        sceneItemEnabled: true,
                        sceneItemLocked: false,
                    },
                ],
            };
        return base(cmd, args);
    });
    show();
    await screen.findByRole("option", { name: "Live" });
    fireEvent.change(screen.getByLabelText("Szene verwalten"), {
        target: { value: "Live" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Gruppe öffnen" }),
    );
    fireEvent.change(screen.getByLabelText("Quellen suchen"), {
        target: { value: "MIC" },
    });
    fireEvent.click(
        await screen.findByRole("button", { name: "Mic bearbeiten" }),
    );
    const forward = await screen.findByRole("button", { name: "Nach vorne" });
    expect(forward).not.toBeDisabled();
    fireEvent.click(forward);
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: {
                action: "set_index",
                sceneName: "Group",
                sceneItemId: 42,
                sceneItemIndex: 1,
            },
        }),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Zurück aus Group" }),
        ).not.toBeDisabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Zurück aus Group" }));
    expect(screen.queryByLabelText("Quelle sichtbar")).not.toBeInTheDocument();
    expect(
        screen.queryByRole("button", { name: "Mic bearbeiten" }),
    ).not.toBeInTheDocument();
});
