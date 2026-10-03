import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { TwitchGoals } from "./TwitchGoals";
const invoke = vi.fn();
const listenMetrics = vi.fn();
const listeners = new Set<() => void>();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    FALLBACK_POLL_MS: 15000,
    listenTwitchGoals: async (fn: () => void) => {
        listeners.add(fn);
        return () => {
            listeners.delete(fn);
        };
    },
    listenTwitchMetrics: (fn: () => void) => listenMetrics(fn),
}));
const goal = {
    title: "Goal",
    current: "0",
    target: "200",
    fontFace: "Segoe UI",
    fontSize: "36",
    currency: "EUR",
    reason: "",
};
const original = {
    Twitch: { FollowerGoal: { Target: 200 }, Future: { keep: true } },
    Obs: { GoalOverlayScene: "Goals" },
};
const snapshot = {
    original,
    draft: {
        overlayScene: "Goals",
        follower: { ...goal, title: "Follower" },
        subscriptions: { ...goal, title: "Subs", target: "25" },
        donation: { ...goal, title: "Support", target: "100" },
    },
    warnings: [] as string[],
};
const count = { value: 30, at: "2026-10-03T12:00:00Z", error: null };
const metrics = {
    connected: true,
    viewerCount: { ...count, value: 0 },
    followers: count,
    subscriptions: { ...count, value: 7, error: "Missing scope" },
    chatters: { ...count, value: 10 },
    title: "Live",
    category: "Game",
    channelError: null,
};
function show(enabled = true) {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <TwitchGoals enabled={enabled} />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    listeners.clear();
    listenMetrics.mockReset();
    listenMetrics.mockResolvedValue(() => {});
    invoke.mockImplementation(async (cmd, args) =>
        cmd === "twitch_goals_snapshot"
            ? structuredClone(snapshot)
            : cmd === "twitch_metrics_snapshot" ||
                cmd === "refresh_twitch_metrics"
              ? metrics
              : {
                    ...snapshot,
                    draft: args.draft,
                    warnings: ["Layout konnte nicht aktualisiert werden"],
                },
    );
});
it("loads goal fields and independent live counts and saves a complete typed draft against its original", async () => {
    show();
    await screen.findByDisplayValue("Follower");
    expect(screen.getByLabelText("Zuschauer")).toHaveTextContent("0");
    expect(screen.getByLabelText("Abonnements")).toHaveTextContent("7");
    expect(screen.getByLabelText("Abonnements")).toHaveTextContent("veraltet");
    expect(
        screen
            .getAllByRole("alert")
            .map((el) => el.textContent)
            .join(" "),
    ).toContain("Missing scope");
    await userEvent.clear(screen.getByLabelText("Follower-Ziel · Ziel"));
    await userEvent.type(
        screen.getByLabelText("Follower-Ziel · Ziel"),
        "250,5",
    );
    await userEvent.type(
        screen.getByLabelText("Donation-Ziel · Grund"),
        "Mikrofon",
    );
    await userEvent.click(
        screen.getByRole("button", { name: "Ziele speichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("save_twitch_goals", {
            draft: {
                ...snapshot.draft,
                follower: { ...snapshot.draft.follower, target: "250,5" },
                donation: { ...snapshot.draft.donation, reason: "Mikrofon" },
            },
            original,
        }),
    );
    expect(await screen.findByText("Ziele gespeichert.")).toBeInTheDocument();
    expect(
        await screen.findByText("Layout konnte nicht aktualisiert werden"),
    ).toBeInTheDocument();
});
it("keeps a dirty draft after external updates and a save conflict and reloads only after confirmation", async () => {
    const view = show(false);
    await screen.findByDisplayValue("Follower");
    await userEvent.type(
        screen.getByLabelText("Follower-Ziel · Bezeichnung"),
        " Entwurf",
    );
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "save_twitch_goals")
            throw new Error("Einstellungen wurden geändert");
        if (cmd === "twitch_goals_snapshot")
            return {
                ...snapshot,
                draft: {
                    ...snapshot.draft,
                    follower: { ...goal, title: "Extern" },
                },
            };
        return metrics;
    });
    act(() => listeners.forEach((fn) => fn()));
    await waitFor(() =>
        expect(
            invoke.mock.calls.filter(([cmd]) => cmd === "twitch_goals_snapshot")
                .length,
        ).toBeGreaterThan(1),
    );
    expect(screen.getByLabelText("Follower-Ziel · Bezeichnung")).toHaveValue(
        "Follower Entwurf",
    );
    await userEvent.click(
        screen.getByRole("button", { name: "Ziele speichern" }),
    );
    expect(
        await screen.findByText(/Einstellungen wurden geändert/),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Follower-Ziel · Bezeichnung")).toHaveValue(
        "Follower Entwurf",
    );
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await userEvent.click(
        screen.getByRole("button", { name: "Ziele neu laden" }),
    );
    expect(screen.getByLabelText("Follower-Ziel · Bezeichnung")).toHaveValue(
        "Follower Entwurf",
    );
    confirm.mockReturnValue(true);
    await userEvent.click(
        screen.getByRole("button", { name: "Ziele neu laden" }),
    );
    await screen.findByDisplayValue("Extern");
    confirm.mockRestore();
    view.unmount();
    expect(listeners.size).toBe(0);
});
it("refreshes native counts and reports failures without clearing loaded goals", async () => {
    show();
    await screen.findByDisplayValue("Follower");
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "refresh_twitch_metrics") throw new Error("Refresh failed");
        return cmd === "twitch_goals_snapshot" ? snapshot : metrics;
    });
    await userEvent.click(
        screen.getByRole("button", { name: "Kanalzahlen aktualisieren" }),
    );
    expect(await screen.findByText(/Refresh failed/)).toBeInTheDocument();
    expect(screen.getByLabelText("Follower-Ziel · Bezeichnung")).toHaveValue(
        "Follower",
    );
});
it("shows failed snapshots and cannot save an absent configuration", async () => {
    invoke.mockRejectedValue(new Error("Snapshot unavailable"));
    show();
    expect(await screen.findByText(/Snapshot unavailable/)).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Ziele speichern" }),
    ).toBeDisabled();
});

it("recovers a failed listener and releases registrations that finish after page exit", async () => {
    listenMetrics.mockRejectedValueOnce(new Error("Listener unavailable"));
    const view = show();
    await screen.findByText(/Listener unavailable/);
    await userEvent.click(
        screen.getByRole("button", { name: "Kanalzahlen aktualisieren" }),
    );
    await waitFor(() => expect(listenMetrics).toHaveBeenCalledTimes(2));
    await waitFor(() =>
        expect(
            screen.queryByText(/Listener unavailable/),
        ).not.toBeInTheDocument(),
    );
    view.unmount();
    let finish!: (fn: () => void) => void;
    listenMetrics.mockImplementation(
        () =>
            new Promise<() => void>((resolve) => {
                finish = resolve;
            }),
    );
    const next = show();
    await waitFor(() => expect(finish).toBeDefined());
    next.unmount();
    const cleanup = vi.fn();
    await act(async () => finish(cleanup));
    expect(cleanup).toHaveBeenCalledOnce();
});
