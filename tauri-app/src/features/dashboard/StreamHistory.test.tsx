import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { StreamHistory } from "./StreamHistory";
const invoke = vi.fn(),
    saveDialog = vi.fn(),
    openPath = vi.fn(),
    listen = vi.fn(),
    copy = vi.fn();
const listeners = new Set<() => void>();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenStreamHistory: (fn: () => void) => listen(fn),
    FALLBACK_POLL_MS: 15000,
}));
vi.mock("@tauri-apps/plugin-dialog", () => ({ save: () => saveDialog() }));
vi.mock("@tauri-apps/plugin-opener", () => ({
    openPath: (path: string) => openPath(path),
}));
const row = {
    SessionId: "s",
    StartedAt: "2026-10-03T10:00:00Z",
    EndedAt: "2026-10-03T11:00:00Z",
    DurationSeconds: 3600,
    PeakViewers: 20,
    AverageViewers: 15,
    FollowersGained: 5,
    ChatMessages: 30,
    AlertsPlayed: 2,
    NewSubscriptions: 2,
    GiftSubscriptions: 3,
    BitsCheered: 42,
    IncomingRaids: 1,
    Title: "<script>Stream</script>",
    Category: "Game",
};
const snapshot = {
    active: null as null | Record<string, unknown>,
    sessions: [row],
    events: [
        {
            TimestampUtc: "2026-10-03T10:00:01Z",
            SessionId: "s",
            Type: "twitch.event",
            Payload: {
                summary: "Alice folgt",
                event: { data: { user_name: "Alice" } },
            },
        },
    ],
    statistics: {
        totalStreams: 1,
        totalDuration: "01:00",
        averageViewers: 15,
        peakViewers: 20,
        followers: 5,
        averageDuration: "01:00",
        categories: [
            { name: "Game", count: 1, seconds: 3600, averageViewers: 15 },
        ],
        development: [row],
    },
    warnings: [] as string[],
    directory: "/history",
};
function show() {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <StreamHistory />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    saveDialog.mockReset();
    openPath.mockReset();
    listen.mockReset();
    copy.mockReset();
    listeners.clear();
    Object.defineProperty(navigator, "clipboard", {
        configurable: true,
        value: { writeText: copy },
    });
    copy.mockResolvedValue(undefined);
    listen.mockImplementation(async (fn) => {
        listeners.add(fn);
        return () => listeners.delete(fn);
    });
    invoke.mockImplementation(async (cmd) =>
        cmd === "stream_history_snapshot"
            ? structuredClone(snapshot)
            : cmd === "latest_stream_summary"
              ? "Stream-Zusammenfassung"
              : cmd === "export_stream_history"
                ? "/export.html"
                : null,
    );
});
it("shows persisted sessions, all counters, category statistics and session-specific events", async () => {
    const user = userEvent.setup();
    show();
    await screen.findByText("<script>Stream</script>");
    expect(screen.getByLabelText("Gesamtzahl Streams")).toHaveTextContent("1");
    expect(
        screen.getByLabelText("Durchschnittliche Zuschauer"),
    ).toHaveTextContent("15,0");
    expect(screen.getByText("Alice folgt")).toBeInTheDocument();
    await user.click(
        screen.getByRole("button", { name: "Ereignisse dieser Sitzung" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("stream_history_snapshot", {
            sessionId: "s",
        }),
    );
    expect(screen.getByText(/Subs: 2/)).toHaveTextContent("Geschenke: 3");
});
it("copies the latest summary, exports HTML only after a chosen path and opens the result", async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, "writeText").mockImplementation(copy);
    show();
    await screen.findByText("<script>Stream</script>");
    await user.click(
        screen.getByRole("button", { name: "Letzte Zusammenfassung kopieren" }),
    );
    await waitFor(() =>
        expect(copy).toHaveBeenCalledWith("Stream-Zusammenfassung"),
    );
    saveDialog.mockResolvedValue(null);
    await user.click(
        screen.getByRole("button", { name: "HTML-Report speichern" }),
    );
    expect(
        invoke.mock.calls.some(([cmd]) => cmd === "export_stream_history"),
    ).toBe(false);
    saveDialog.mockResolvedValue("/export.html");
    await user.click(
        screen.getByRole("button", { name: "HTML-Report speichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("export_stream_history", {
            format: "html",
            path: "/export.html",
        }),
    );
    await user.click(
        await screen.findByRole("button", { name: "Export öffnen" }),
    );
    expect(openPath).toHaveBeenCalledWith("/export.html");
});
it("shows recovered active sessions and storage errors and retries explicitly", async () => {
    invoke.mockImplementation(async (cmd) =>
        cmd === "stream_history_snapshot"
            ? {
                  ...snapshot,
                  active: { ...row, Recovered: true },
                  warnings: ["Datei nicht beschreibbar"],
              }
            : cmd === "retry_stream_history"
              ? Promise.reject(Error("Weiterhin gesperrt"))
              : null,
    );
    const user = userEvent.setup();
    show();
    expect(
        await screen.findByText(/Sitzung nach Neustart/),
    ).toBeInTheDocument();
    expect(screen.getByText("Datei nicht beschreibbar")).toBeInTheDocument();
    await user.click(
        screen.getByRole("button", { name: "Speicherung erneut versuchen" }),
    );
    expect(await screen.findByText(/Weiterhin gesperrt/)).toBeInTheDocument();
});
it("refreshes history after events and unsubscribes late listeners", async () => {
    const view = show();
    await screen.findByText("Alice folgt");
    const count = invoke.mock.calls.length;
    act(() => listeners.forEach((fn) => fn()));
    await waitFor(() =>
        expect(invoke.mock.calls.length).toBeGreaterThan(count),
    );
    view.unmount();
    expect(listeners.size).toBe(0);
    const stop = vi.fn();
    let resolve!: (fn: () => void) => void;
    listen.mockImplementation(
        () =>
            new Promise((r) => {
                resolve = r;
            }),
    );
    const late = show();
    late.unmount();
    await act(async () => resolve(stop));
    expect(stop).toHaveBeenCalledOnce();
});
