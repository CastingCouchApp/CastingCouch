import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { TwitchModeration } from "./TwitchModeration";
const invoke = vi.fn(),
    save = vi.fn(),
    openPath = vi.fn();
const listeners = new Set<() => void>();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenTwitchModeration: async (fn: () => void) => {
        listeners.add(fn);
        return () => {
            listeners.delete(fn);
        };
    },
    FALLBACK_POLL_MS: 15000,
}));
vi.mock("@tauri-apps/plugin-dialog", () => ({ save: () => save() }));
vi.mock("@tauri-apps/plugin-opener", () => ({
    openPath: (path: string) => openPath(path),
}));
function show(enabled = true, selected?: string) {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <TwitchModeration enabled={enabled} selectedUser={selected} />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    save.mockReset();
    openPath.mockReset();
    listeners.clear();
    invoke.mockImplementation(async (cmd) =>
        cmd === "twitch_moderation_snapshot"
            ? { entries: [] }
            : {
                  applied: true,
                  message: "Twitch-Aktion erfolgreich",
                  warnings: [],
              },
    );
    openPath.mockResolvedValue(undefined);
});
it("selects a user, applies presets and sends timeout, ban and unban through the shared runtime", async () => {
    show(true, "Alice");
    const user = userEvent.setup();
    expect(screen.getByLabelText("Moderationsbenutzer")).toHaveValue("Alice");
    await user.click(screen.getByRole("button", { name: "24 Std." }));
    expect(screen.getByLabelText("Timeout in Minuten")).toHaveValue(1440);
    await user.type(screen.getByLabelText("Moderationsgrund"), "Spam");
    await user.click(screen.getByRole("button", { name: "Timeout anwenden" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_moderate", {
            action: {
                action: "timeout",
                user: "Alice",
                byId: false,
                minutes: 1440,
                reason: "Spam",
            },
        }),
    );
    await screen.findByText("Twitch-Aktion erfolgreich");
    await user.click(screen.getByRole("button", { name: "Benutzer bannen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_moderate", {
            action: {
                action: "ban",
                user: "Alice",
                byId: false,
                reason: "Spam",
            },
        }),
    );
    await user.click(
        screen.getByRole("button", { name: "Ban oder Timeout aufheben" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_moderate", {
            action: { action: "unban", user: "Alice", byId: false },
        }),
    );
});
it("validates drafts and preserves them after a denied action while showing storage warnings", async () => {
    show();
    const user = userEvent.setup();
    expect(
        screen.getByRole("button", { name: "Benutzer bannen" }),
    ).toBeDisabled();
    await user.type(screen.getByLabelText("Moderationsbenutzer"), "Bob");
    await user.clear(screen.getByLabelText("Timeout in Minuten"));
    await user.type(screen.getByLabelText("Timeout in Minuten"), "0");
    expect(
        screen.getByRole("button", { name: "Timeout anwenden" }),
    ).toBeDisabled();
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "twitch_moderate") throw new Error("Missing scope");
        return { entries: [] };
    });
    await user.click(screen.getByRole("button", { name: "Benutzer bannen" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Missing scope");
    expect(screen.getByLabelText("Moderationsbenutzer")).toHaveValue("Bob");
    invoke.mockImplementation(async (cmd) =>
        cmd === "twitch_moderation_snapshot"
            ? { entries: [] }
            : {
                  applied: true,
                  message: "Bob wurde gebannt.",
                  warnings: ["Protokoll nicht gespeichert"],
              },
    );
    await user.click(screen.getByRole("button", { name: "Benutzer bannen" }));
    expect(await screen.findByRole("status")).toHaveTextContent(
        "Bob wurde gebannt.",
    );
    expect(screen.getByRole("alert")).toHaveTextContent(
        "Protokoll nicht gespeichert",
    );
});
it("clears only the view, exports with the native dialog and refreshes external log changes", async () => {
    let entries = ["03.10.2026 · BAN · @Alice"];
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "clear_twitch_moderation_view") {
            entries = [];
            return;
        }
        return { entries };
    });
    const view = show(false);
    const user = userEvent.setup();
    await screen.findByText(entries[0]);
    expect(
        screen.getByRole("button", { name: "Timeout anwenden" }),
    ).toBeDisabled();
    save.mockResolvedValueOnce(null);
    await user.click(
        screen.getByRole("button", {
            name: "Moderationsprotokoll exportieren",
        }),
    );
    expect(
        invoke.mock.calls.some(
            ([cmd]) => cmd === "export_twitch_moderation_log",
        ),
    ).toBe(false);
    save.mockResolvedValueOnce("C:/export.txt");
    await user.click(
        screen.getByRole("button", {
            name: "Moderationsprotokoll exportieren",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("export_twitch_moderation_log", {
            path: "C:/export.txt",
        }),
    );
    expect(openPath).toHaveBeenCalledWith("C:/export.txt");
    await user.click(
        screen.getByRole("button", { name: "Protokollansicht leeren" }),
    );
    await waitFor(() =>
        expect(
            screen.queryByText("03.10.2026 · BAN · @Alice"),
        ).not.toBeInTheDocument(),
    );
    entries = ["Neue Moderation"];
    act(() => listeners.forEach((fn) => fn()));
    await screen.findByText("Neue Moderation");
    view.unmount();
    expect(listeners.size).toBe(0);
});
it("exposes export and snapshot failures without claiming a successful export", async () => {
    invoke.mockRejectedValue(new Error("Logs unreadable"));
    show();
    await screen.findByRole("alert");
    save.mockResolvedValue("C:/export.txt");
    await userEvent.click(
        screen.getByRole("button", {
            name: "Moderationsprotokoll exportieren",
        }),
    );
    await waitFor(() =>
        expect(
            screen
                .getAllByRole("alert")
                .map((el) => el.textContent)
                .join(" "),
        ).toContain("Logs unreadable"),
    );
    expect(openPath).not.toHaveBeenCalled();
    expect(screen.queryByText(/Protokoll exportiert/)).not.toBeInTheDocument();
});
