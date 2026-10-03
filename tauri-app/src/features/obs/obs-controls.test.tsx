import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    fireEvent,
    render,
    screen,
    waitFor,
    within,
} from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { ObsControls } from "./ObsControls";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenStreamEnd: async () => () => {},
    listenStreamEndSettings: async () => () => {},
}));
function show() {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <ObsControls enabled />
        </QueryClientProvider>,
    );
}
describe("OBS output availability", () => {
    it("keeps an incomplete stream-status response unknown and disables stream mutations", async () => {
        invoke.mockReset();
        invoke.mockResolvedValue({
            stream: {},
            record: { outputActive: false },
        });
        show();
        await waitFor(() =>
            expect(
                screen.getByRole("button", { name: "Aufnahme starten" }),
            ).toBeEnabled(),
        );
        expect(
            await screen.findByText("Streamstatus unbekannt"),
        ).toBeInTheDocument();
        expect(
            screen.getByRole("button", { name: "Stream starten" }),
        ).toBeDisabled();
        expect(
            screen.getByRole("button", { name: "Aufnahme starten" }),
        ).toBeEnabled();
    });
    it("starts only after confirmation and reports an unsuccessful start without claiming live", async () => {
        invoke.mockReset();
        invoke.mockImplementation(async (command: string) => {
            if (command === "obs_output_status")
                return { stream: { outputActive: false } };
            if (command === "obs_control")
                throw Error("Startszene fehlt in OBS");
            return null;
        });
        const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
        show();
        const start = await screen.findByRole("button", {
            name: "Stream starten",
        });
        await waitFor(() => expect(start).toBeEnabled());
        fireEvent.click(start);
        expect(confirm).toHaveBeenCalledWith("OBS-Stream wirklich starten?");
        expect(
            invoke.mock.calls.some(([command]) => command === "obs_control"),
        ).toBe(false);
        confirm.mockReturnValue(true);
        fireEvent.click(start);
        expect(await screen.findByRole("alert")).toHaveTextContent(
            "Startszene fehlt in OBS",
        );
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: { action: "start_stream" },
        });
        expect(screen.getByText("Stream gestoppt")).toBeInTheDocument();
        confirm.mockRestore();
    });
    it("opens the shared assistant and sends no OBS stop before an explicit choice", async () => {
        invoke.mockReset();
        invoke.mockImplementation(async (command: string, args: any) => {
            if (command === "obs_output_status")
                return { stream: { outputActive: true } };
            if (command === "stream_end_status")
                return {
                    active: false,
                    phase: "idle",
                    status: "Kein Streamende geplant",
                    warnings: [],
                };
            if (
                command === "stream_end_snapshot" ||
                command === "save_stream_end_preferences"
            )
                return {
                    original: {},
                    endScene: "End",
                    raidChannels: [],
                    outgoingRaid: { available: false, error: "Getrennt" },
                    warnings: [],
                    draft: args?.draft ?? {
                        mode: "EndSceneThenStop",
                        endSceneSeconds: 60,
                        raidOnStreamEnd: false,
                        selectedRaidChannel: "",
                        raidCountdownSeconds: 90,
                        raidStartTimeoutSeconds: 120,
                        stopStreamAfterRaid: true,
                        stopMusicAfterRaid: true,
                        plannedSeconds: 1800,
                        plannedMinutes: 30,
                    },
                };
            if (command === "start_stream_end")
                return { active: true, phase: "preparing", warnings: [] };
            return null;
        });
        show();
        fireEvent.click(
            await screen.findByRole("button", { name: "Stream stoppen" }),
        );
        const dialog = await screen.findByRole("dialog", {
            name: "Streamende und Raid",
        });
        expect(invoke.mock.calls.some(([cmd]) => cmd === "obs_control")).toBe(
            false,
        );
        fireEvent.change(
            await within(dialog).findByLabelText("Streamende-Modus"),
            { target: { value: "Immediate" } },
        );
        fireEvent.click(
            within(dialog).getByRole("button", { name: "Streamende starten" }),
        );
        await waitFor(() =>
            expect(invoke).toHaveBeenCalledWith("start_stream_end", {
                planned: false,
                reviewed: {},
            }),
        );
        expect(invoke.mock.calls.some(([cmd]) => cmd === "obs_control")).toBe(
            false,
        );
    });
    it("keeps stream controls available when the virtual camera is unavailable", async () => {
        invoke.mockResolvedValue({
            stream: { outputActive: true },
            record: { outputActive: false },
            replay: { outputActive: false },
            camera: null,
            stats: { activeFps: 60 },
            errors: { camera: "Camera unavailable" },
        });
        show();
        expect(await screen.findByText("Stream läuft")).toBeInTheDocument();
        expect(
            screen.getByRole("button", { name: "Stream stoppen" }),
        ).toBeEnabled();
        expect(
            screen.getByRole("button", { name: "Virtuelle Kamera starten" }),
        ).toBeDisabled();
        expect(screen.getByText(/Camera unavailable/)).toBeInTheDocument();
    });
    it("does not present a failed status request as a stopped stream", async () => {
        invoke.mockRejectedValue(new Error("Disconnected"));
        show();
        expect(await screen.findByRole("alert")).toHaveTextContent(
            "Disconnected",
        );
        expect(screen.queryByText("Stream gestoppt")).not.toBeInTheDocument();
        expect(
            screen.getByRole("button", { name: "Stream starten" }),
        ).toBeDisabled();
    });
});
