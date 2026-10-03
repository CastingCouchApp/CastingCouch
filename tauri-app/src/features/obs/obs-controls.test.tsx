import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    act,
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
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    const view = render(
        <QueryClientProvider client={client}>
            <ObsControls enabled />
        </QueryClientProvider>,
    );
    return { client, ...view };
}
it("shows actual output timecodes, recording/replay/camera state and full C# statistics", async () => {
    invoke.mockReset().mockResolvedValue({
        stream: { outputActive: true, outputTimecode: "01:02:03.004" },
        record: {
            outputActive: true,
            outputPaused: true,
            outputDuration: 90000,
        },
        replay: { outputActive: true },
        camera: { outputActive: false },
        stats: {
            activeFps: 59.94,
            cpuUsage: 12.5,
            memoryUsage: 1024,
            renderSkippedFrames: 3,
            renderTotalFrames: 2000,
            outputSkippedFrames: 4,
            outputTotalFrames: 1800,
        },
    });
    show();
    expect(
        await screen.findByText("Streamlaufzeit: 01:02:03.004"),
    ).toBeInTheDocument();
    expect(
        screen.getByText("Aufnahme: Pausiert · 00:01:30"),
    ).toBeInTheDocument();
    expect(screen.getByText("Replay Buffer: Aktiv")).toBeInTheDocument();
    expect(screen.getByText("Virtuelle Kamera: Gestoppt")).toBeInTheDocument();
    for (const text of [
        "CPU: 12.5 %",
        "FPS: 59.9",
        "RAM: 1024 MB",
        "Render-Lag: 3/2000",
        "Encoding-Lag: 4/1800",
    ])
        expect(screen.getByText(text)).toBeInTheDocument();
});
it("keeps missing statistics unknown and does not allow pausing with unknown pause status", async () => {
    invoke.mockReset().mockResolvedValue({
        stream: null,
        record: { outputActive: true },
        stats: { activeFps: -1 },
        errors: { stream: "Streamstatus fehlgeschlagen" },
    });
    show();
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Aufnahme stoppen" }),
        ).toBeEnabled(),
    );
    expect(screen.getByText("Aufnahme: Status unbekannt")).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Aufnahme pausieren" }),
    ).toBeDisabled();
    expect(
        screen.getByRole("button", { name: "Aufnahme stoppen" }),
    ).toBeEnabled();
    for (const text of [
        "CPU: Unbekannt",
        "FPS: Unbekannt",
        "RAM: Unbekannt",
        "Render-Lag: Unbekannt",
        "Encoding-Lag: Unbekannt",
    ])
        expect(screen.getByText(text)).toBeInTheDocument();
});
it("hides cached live statistics after query failure and recovers with an explicit refresh", async () => {
    let offline = false;
    const connected = {
        stream: { outputActive: true, outputDuration: 60000 },
        record: { outputActive: false },
        stats: { cpuUsage: 12.5, activeFps: 60 },
    };
    invoke.mockReset().mockImplementation(async () => {
        if (offline) throw Error("OBS-Verbindung unterbrochen");
        return connected;
    });
    const { client, rerender } = show();
    expect(await screen.findByText("CPU: 12.5 %")).toBeInTheDocument();
    offline = true;
    await act(() => client.invalidateQueries({ queryKey: ["obs-outputs"] }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
        "OBS-Verbindung unterbrochen",
    );
    expect(screen.queryByText("CPU: 12.5 %")).not.toBeInTheDocument();
    expect(screen.getByText("CPU: Unbekannt")).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Stream starten" }),
    ).toBeDisabled();
    offline = false;
    fireEvent.click(
        screen.getByRole("button", { name: "OBS-Status aktualisieren" }),
    );
    expect(
        await screen.findByText("Streamlaufzeit: 00:01:00"),
    ).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Stream stoppen" }),
    ).toBeEnabled();
    rerender(
        <QueryClientProvider client={client}>
            <ObsControls enabled={false} />
        </QueryClientProvider>,
    );
    expect(screen.queryByText("CPU: 12.5 %")).not.toBeInTheDocument();
    expect(screen.getByText("OBS nicht verbunden")).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "OBS-Status aktualisieren" }),
    ).toBeDisabled();
});
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
