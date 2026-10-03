import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import reference from "../../../src-tauri/crates/ccs-modules/tests/fixtures/creator-intelligence.json";
import { beforeEach, expect, it, vi } from "vitest";
import { CreatorIntelligence } from "./CreatorIntelligence";

const invoke = vi.fn(),
    openPath = vi.fn(),
    listen = vi.fn();
const listeners = new Set<() => void>();
let snapshot = {
    ...reference,
    recording: true,
    warnings: [] as string[],
    directory: "/intelligence",
};
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenStreamHistory: (fn: () => void) => listen(fn),
    FALLBACK_POLL_MS: 15000,
}));
vi.mock("@tauri-apps/plugin-opener", () => ({
    openPath: (path: string) => openPath(path),
}));
function show() {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <CreatorIntelligence />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    snapshot = {
        ...reference,
        recording: true,
        warnings: [],
        directory: "/intelligence",
    };
    invoke.mockReset();
    openPath.mockReset();
    listen.mockReset();
    listeners.clear();
    listen.mockImplementation(async (fn) => {
        listeners.add(fn);
        return () => listeners.delete(fn);
    });
    invoke.mockImplementation(async (cmd) =>
        cmd === "creator_intelligence_snapshot"
            ? structuredClone(snapshot)
            : cmd === "generate_creator_weekly_report"
              ? "/report.html"
              : null,
    );
});
it("shows original analysis metrics, content, correlations, actions and experiments and changes timeframe", async () => {
    const user = userEvent.setup();
    show();
    await screen.findByLabelText("Creator Score im Zeitraum");
    expect(
        screen.getByLabelText("Creator Score im Zeitraum"),
    ).not.toHaveTextContent("NaN");
    expect(screen.getByLabelText("Vollständige Sessions")).toHaveTextContent(
        "12",
    );
    expect(screen.getAllByText("Main").length).toBeGreaterThan(0);
    expect(screen.getByText(/Chat test/)).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Analysezeitraum"), "7");
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("creator_intelligence_snapshot", {
            lookbackDays: 7,
        }),
    );
});
it("preserves rejected notes and retries the same request without duplicates", async () => {
    const user = userEvent.setup();
    show();
    await screen.findByLabelText("Creator Score im Zeitraum");
    await user.type(screen.getByLabelText("Streamnotiz"), "Interview");
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "record_creator_note") throw "Schreibfehler";
        return structuredClone(snapshot);
    });
    await user.click(screen.getByRole("button", { name: "Notiz speichern" }));
    await screen.findByText("Schreibfehler");
    expect(screen.getByLabelText("Streamnotiz")).toHaveValue("Interview");
    const first = invoke.mock.calls.find(
        ([cmd]) => cmd === "record_creator_note",
    )![1];
    invoke.mockImplementation(async (cmd) =>
        cmd === "creator_intelligence_snapshot"
            ? structuredClone(snapshot)
            : null,
    );
    await user.click(screen.getByRole("button", { name: "Notiz speichern" }));
    await waitFor(() =>
        expect(screen.getByLabelText("Streamnotiz")).toHaveValue(""),
    );
    const notes = invoke.mock.calls.filter(
        ([cmd]) => cmd === "record_creator_note",
    );
    expect(notes[1][1]).toEqual(first);
});
it("completes measurable actions, starts experiments and generates and opens the weekly report", async () => {
    snapshot = structuredClone(snapshot);
    snapshot.actions.Items.find((r) => r.Id === "custom-engagement")!.Status =
        "Offen";
    snapshot.experiments.Rows = snapshot.experiments.Rows.filter(
        (r) => r.Status !== "Aktiv",
    );
    const user = userEvent.setup();
    show();
    await screen.findByLabelText("Creator Score im Zeitraum");
    const row = screen
        .getAllByText("Chat goal")
        .map((node) => node.closest("li"))
        .find(Boolean)!;
    await user.click(
        within(row).getByRole("button", { name: "Experiment starten" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("start_creator_experiment", {
            actionId: "custom-engagement",
        }),
    );
    await user.click(
        within(row).getByRole("button", { name: "Als erledigt markieren" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("complete_creator_action", {
            actionId: "custom-engagement",
        }),
    );
    await user.click(
        screen.getByRole("button", { name: "Wochenbericht erstellen" }),
    );
    await user.click(
        await screen.findByRole("button", { name: "Wochenbericht öffnen" }),
    );
    await waitFor(() => expect(openPath).toHaveBeenCalledWith("/report.html"));
});
it("refreshes on journal events, shows warnings and disables notes after the session ends", async () => {
    const user = userEvent.setup();
    const view = show();
    await screen.findByLabelText("Creator Score im Zeitraum");
    await user.type(screen.getByLabelText("Streamnotiz"), "Draft");
    snapshot.recording = false;
    snapshot.warnings = ["Original bleibt erhalten"];
    act(() => {
        for (const fn of listeners) fn();
    });
    await screen.findByText("Original bleibt erhalten");
    expect(
        screen.getByRole("button", { name: "Notiz speichern" }),
    ).toBeDisabled();
    expect(screen.getByLabelText("Streamnotiz")).toHaveValue("Draft");
    view.unmount();
    expect(listeners.size).toBe(0);
});
it("shows analysis and listener errors with refresh available", async () => {
    invoke.mockRejectedValue("Journal nicht lesbar");
    listen.mockRejectedValue("Live-Ereignisse ausgefallen");
    show();
    await screen.findByText("Journal nicht lesbar");
    expect(screen.getByText(/Live-Ereignisse ausgefallen/)).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Analyse aktualisieren" }),
    ).toBeEnabled();
});

it("keeps a newer note draft when an earlier note finishes saving", async () => {
    const user = userEvent.setup();
    show();
    await screen.findByLabelText("Creator Score im Zeitraum");
    let finish!: () => void;
    invoke.mockImplementation(async (cmd) =>
        cmd === "record_creator_note"
            ? new Promise<void>((resolve) => {
                  finish = resolve;
              })
            : structuredClone(snapshot),
    );
    await user.type(screen.getByLabelText("Streamnotiz"), "First");
    await user.click(screen.getByRole("button", { name: "Notiz speichern" }));
    await user.clear(screen.getByLabelText("Streamnotiz"));
    await user.type(screen.getByLabelText("Streamnotiz"), "Next");
    act(() => finish());
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Notiz speichern" }),
        ).toBeEnabled(),
    );
    expect(screen.getByLabelText("Streamnotiz")).toHaveValue("Next");
});
