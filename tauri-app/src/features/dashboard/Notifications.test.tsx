import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    act,
    fireEvent,
    render,
    screen,
    waitFor,
} from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { Notifications } from "./Notifications";
import { cardVisible } from "./dashboard-types";
import dashboardDefault from "./dashboard-default.json";
const invoke = vi.fn();
let change = () => {};
const unlisten = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenNotificationsChanged: async (callback: () => void) => {
        change = callback;
        return unlisten;
    },
    FALLBACK_POLL_MS: 10000,
}));
beforeEach(() => {
    invoke.mockReset();
    unlisten.mockReset();
    change = () => {};
});
const snapshot = (message = "Actual OBS start") => ({
    entries: [
        {
            timestamp: "2026-10-03T12:00:00Z",
            severity: "Info",
            message,
            isRead: false,
        },
    ],
    total: 1,
    unreadCount: 1,
    warnings: [],
    recoveryBackup: null,
});
function show() {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <Notifications />
        </QueryClientProvider>,
    );
}
it("filters, marks read, confirms clearing and refreshes from native events without leaking listeners", async () => {
    invoke.mockResolvedValue(snapshot());
    const view = show();
    expect(await screen.findByText("Actual OBS start")).toBeInTheDocument();
    expect(screen.getByText("1 ungelesen")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Benachrichtigungen filtern"), {
        target: { value: "Fehler" },
    });
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("notifications_snapshot", {
            filter: "Fehler",
        }),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Alle als gelesen markieren" }),
        ).toBeEnabled(),
    );
    fireEvent.click(
        screen.getByRole("button", { name: "Alle als gelesen markieren" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "notifications_mark_read",
            undefined,
        ),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Benachrichtigungen leeren" }),
        ).toBeEnabled(),
    );
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    fireEvent.click(
        screen.getByRole("button", { name: "Benachrichtigungen leeren" }),
    );
    expect(
        invoke.mock.calls.some(([cmd]) => cmd === "notifications_clear"),
    ).toBe(false);
    confirm.mockReturnValue(true);
    fireEvent.click(
        screen.getByRole("button", { name: "Benachrichtigungen leeren" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("notifications_clear", undefined),
    );
    invoke.mockResolvedValue(snapshot("New actual event"));
    await act(async () => change());
    expect(await screen.findByText("New actual event")).toBeInTheDocument();
    view.unmount();
    expect(unlisten).toHaveBeenCalledOnce();
    confirm.mockRestore();
});
it("respects legacy Notifications visibility and hides the card in focus mode", () => {
    const draft = structuredClone(dashboardDefault);
    expect(cardVisible(draft, "Notifications", false)).toBe(true);
    expect(cardVisible(draft, "Notifications", true)).toBe(false);
    draft.preferences.showNotifications = false;
    expect(cardVisible(draft, "Notifications", false)).toBe(false);
});
it("reflects persisted read and clear results", async () => {
    let current = snapshot();
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "notifications_snapshot") return structuredClone(current);
        if (cmd === "notifications_mark_read") {
            current.unreadCount = 0;
            current.entries[0].isRead = true;
        }
        if (cmd === "notifications_clear") {
            current.total = 0;
            current.entries = [];
        }
        return null;
    });
    const view = show();
    expect(await screen.findByText("Actual OBS start")).toBeInTheDocument();
    fireEvent.click(
        screen.getByRole("button", { name: "Alle als gelesen markieren" }),
    );
    expect(await screen.findByText("1 Meldungen")).toBeInTheDocument();
    expect(screen.queryByLabelText("Ungelesen")).not.toBeInTheDocument();
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Benachrichtigungen leeren" }),
        ).toBeEnabled(),
    );
    vi.spyOn(window, "confirm").mockReturnValue(true);
    fireEvent.click(
        screen.getByRole("button", { name: "Benachrichtigungen leeren" }),
    );
    expect(
        await screen.findByText("Keine Benachrichtigungen."),
    ).toBeInTheDocument();
    view.unmount();
});
it("shows persistence warnings and failed edits and permits explicit saving retry", async () => {
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "notifications_snapshot")
            return { ...snapshot(), warnings: ["Datei nicht gespeichert"] };
        if (cmd === "notifications_mark_read") throw Error("Disk full");
        return null;
    });
    show();
    expect(
        await screen.findByText("Datei nicht gespeichert"),
    ).toBeInTheDocument();
    fireEvent.click(
        screen.getByRole("button", { name: "Alle als gelesen markieren" }),
    );
    expect(await screen.findByText(/Disk full/)).toBeInTheDocument();
    expect(screen.getByText("1 ungelesen")).toBeInTheDocument();
    fireEvent.click(
        screen.getByRole("button", { name: "Speichern erneut versuchen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("notifications_retry", undefined),
    );
});
