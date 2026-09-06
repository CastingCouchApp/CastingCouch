import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, it, expect, vi } from "vitest";
import { ObsManager } from "./ObsManager";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
function show(enabled = true) {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <ObsManager enabled={enabled} />
        </QueryClientProvider>,
    );
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
