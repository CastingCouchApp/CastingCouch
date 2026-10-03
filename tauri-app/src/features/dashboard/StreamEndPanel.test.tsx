import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    act,
    fireEvent,
    render,
    screen,
    waitFor,
} from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import type { StreamEndSnapshot } from "../../lib/command-contract";
import { StreamEndPanel } from "./StreamEndPanel";
const invoke = vi.fn();
let change: (status: StreamEndSnapshot) => void = () => {};
vi.mock("../../lib/api", () => ({
    tauriInvoke: (command: string, args: unknown) => invoke(command, args),
    listenStreamEnd: async (fn: (status: StreamEndSnapshot) => void) => {
        change = fn;
        return () => {};
    },
    listenStreamEndSettings: async () => () => {},
}));
const idle: StreamEndSnapshot = {
    runId: 0,
    active: false,
    phase: "idle",
    status: "Kein Streamende geplant",
    remainingSeconds: 0,
    totalSeconds: 0,
    attempt: 0,
    targetLogin: "",
    targetDisplayName: "",
    canRaidNow: false,
    raidPending: false,
    pendingAction: null,
    broadcasterId: "",
    broadcasterLogin: "",
    error: null,
    warnings: [],
};
const config = () => ({
    original: { Twitch: { Future: 7 } },
    draft: {
        mode: "EndSceneThenStop",
        endSceneSeconds: 60,
        raidOnStreamEnd: false,
        selectedRaidChannel: "target",
        raidCountdownSeconds: 90,
        raidStartTimeoutSeconds: 120,
        stopStreamAfterRaid: true,
        stopMusicAfterRaid: true,
        plannedSeconds: 1800,
        plannedMinutes: 30,
    },
    raidChannels: ["target"],
    endScene: "End",
    outgoingRaid: { available: true, broadcasterId: "own", error: null },
    warnings: [],
});
function show(
    props: Partial<React.ComponentProps<typeof StreamEndPanel>> = {},
) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return render(
        <QueryClientProvider client={client}>
            <StreamEndPanel enabled live defaultExpanded {...props} />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    change = () => {};
    invoke.mockImplementation(async (command: string, args: any) => {
        if (command === "stream_end_status") return { ...idle };
        if (command === "stream_end_snapshot") return config();
        if (command === "save_stream_end_preferences")
            return { ...config(), draft: args.draft };
        if (command === "start_stream_end")
            return { ...idle, active: true, runId: 1, phase: "preparing" };
        if (command === "stream_end_control")
            return {
                ...idle,
                active: true,
                runId: 1,
                phase: "raid_countdown",
                raidPending: true,
                pendingAction: args.action,
            };
        return null;
    });
});
it("requires a known idle runtime and uses legacy minutes when seconds are unset", async () => {
    let finish: (value: StreamEndSnapshot) => void = () => {};
    const waiting = new Promise<StreamEndSnapshot>((resolve) => {
        finish = resolve;
    });
    invoke.mockImplementation(async (command: string, args: any) => {
        if (command === "stream_end_status") return waiting;
        if (command === "stream_end_snapshot") {
            const value = config();
            value.draft.plannedSeconds = 0;
            return value;
        }
        if (command === "save_stream_end_preferences")
            return { ...config(), draft: args.draft };
        if (command === "start_stream_end")
            return { ...idle, active: true, phase: "scheduled" };
        return null;
    });
    show();
    expect(
        await screen.findByLabelText("Geplantes Streamende in Sekunden"),
    ).toHaveValue(1800);
    expect(
        screen.getByRole("button", { name: "Streamende starten" }),
    ).toBeDisabled();
    await act(async () => finish(idle));
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Streamende planen" }),
        ).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Streamende planen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "start_stream_end",
            expect.objectContaining({ planned: true, seconds: 1800 }),
        ),
    );
});
it("saves the concrete draft and starts the chosen native mode with the reviewed snapshot", async () => {
    show();
    await screen.findByLabelText("Streamende-Modus");
    fireEvent.change(screen.getByLabelText("Streamende-Modus"), {
        target: { value: "EndSceneRaidThenStop" },
    });
    fireEvent.change(screen.getByLabelText("Raid-Ziel"), {
        target: { value: "other" },
    });
    fireEvent.change(screen.getByLabelText("Endszene in Sekunden"), {
        target: { value: "25" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Streamende starten" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "start_stream_end",
            expect.objectContaining({
                planned: false,
                reviewed: expect.any(Object),
            }),
        ),
    );
    expect(invoke).toHaveBeenCalledWith(
        "save_stream_end_preferences",
        expect.objectContaining({
            original: config().original,
            draft: expect.objectContaining({
                mode: "EndSceneRaidThenStop",
                selectedRaidChannel: "other",
                endSceneSeconds: 25,
            }),
        }),
    );
});
it("missing outgoing subscription prevents raid start and unknown OBS status prevents any start", async () => {
    invoke.mockImplementation(async (command: string) =>
        command === "stream_end_status"
            ? idle
            : {
                  ...config(),
                  draft: { ...config().draft, mode: "EndSceneRaidThenStop" },
                  outgoingRaid: { available: false, error: "outgoing denied" },
              },
    );
    const view = show();
    await screen.findByText(/outgoing denied/);
    expect(
        screen.getByRole("button", { name: "Streamende starten" }),
    ).toBeDisabled();
    view.unmount();
    show({ live: undefined });
    await screen.findByLabelText("Streamende-Modus");
    expect(
        screen.getByRole("button", { name: "Streamende starten" }),
    ).toBeDisabled();
});
it("countdown zero waits for native proof and queued abort does not claim the stream is stopped", async () => {
    const active = {
        ...idle,
        runId: 3,
        active: true,
        phase: "awaiting_raid",
        status: "Bestätigung wird abgewartet",
        raidPending: true,
        targetLogin: "target",
    };
    invoke.mockImplementation(async (command: string, args: any) =>
        command === "stream_end_status"
            ? active
            : command === "stream_end_control"
              ? { ...active, pendingAction: args.action }
              : config(),
    );
    const close = vi.fn();
    show({ onClose: close });
    await screen.findByText("Bestätigung wird abgewartet");
    fireEvent.click(
        screen.getByRole("button", { name: "Abbrechen und schließen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("stream_end_control", {
            action: "abort",
        }),
    );
    expect(close).not.toHaveBeenCalled();
    expect(screen.queryByText("Stream beendet")).not.toBeInTheDocument();
    act(() =>
        change({
            ...active,
            active: false,
            phase: "aborted",
            pendingAction: null,
            status: "Streamende abgebrochen; Stream läuft weiter",
        }),
    );
    await waitFor(() => expect(close).toHaveBeenCalledOnce());
});
it("a failed save retains the edited draft and never starts the stream-end flow", async () => {
    invoke.mockImplementation(async (command: string) => {
        if (command === "stream_end_status") return idle;
        if (command === "save_stream_end_preferences") throw Error("Konflikt");
        return config();
    });
    show();
    await screen.findByLabelText("Endszene in Sekunden");
    fireEvent.change(screen.getByLabelText("Endszene in Sekunden"), {
        target: { value: "17" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Streamende starten" }));
    await screen.findByText(/Konflikt/);
    expect(screen.getByLabelText("Endszene in Sekunden")).toHaveValue(17);
    expect(
        invoke.mock.calls.some(([command]) => command === "start_stream_end"),
    ).toBe(false);
});
it("planned end saves its independent raid preference and countdown without leaving the app", async () => {
    show();
    await screen.findByLabelText("Geplantes Streamende in Sekunden");
    fireEvent.click(screen.getByLabelText("Bei geplantem Streamende raiden"));
    fireEvent.change(
        screen.getByLabelText("Geplantes Streamende in Sekunden"),
        { target: { value: "300" } },
    );
    fireEvent.click(screen.getByRole("button", { name: "Streamende planen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "start_stream_end",
            expect.objectContaining({ planned: true, seconds: 300 }),
        ),
    );
    expect(invoke).toHaveBeenCalledWith(
        "save_stream_end_preferences",
        expect.objectContaining({
            draft: expect.objectContaining({
                raidOnStreamEnd: true,
                plannedSeconds: 300,
            }),
        }),
    );
});
