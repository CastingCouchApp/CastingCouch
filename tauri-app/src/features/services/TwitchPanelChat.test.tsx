import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { TwitchPanel } from "./TwitchPanel";
const invoke = vi.fn();
const listen = vi.fn();
const listeners = new Set<(event: Record<string, unknown>) => void>();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) =>
        cmd === "twitch_moderation_snapshot"
            ? Promise.resolve({ entries: [] })
            : invoke(cmd, args),
    listenTwitchEvents: (fn: (event: Record<string, unknown>) => void) =>
        listen(fn),
    listenTwitchModeration: async (fn: () => void) => {
        listeners.add(fn);
        return () => {
            listeners.delete(fn);
        };
    },
    FALLBACK_POLL_MS: 15000,
}));
vi.mock("./ChatCatalogStatus", () => ({ ChatCatalogStatusPanel: () => null }));
vi.mock("./TwitchRewards", () => ({ TwitchRewards: () => null }));
vi.mock("./TwitchVotes", () => ({ TwitchVotes: () => null }));
vi.mock("./TwitchGoals", () => ({ TwitchGoals: () => null }));
vi.mock("./TwitchRaids", () => ({ TwitchRaids: () => null }));
function event(text: string, id = "message") {
    return {
        source: "twitch",
        type: "channel.chat.message",
        at: "2026-10-03T12:00:00Z",
        summary: text,
        data: {
            userName: "Alice",
            userLogin: "alice",
            userId: "u",
            messageId: id,
            text,
        },
    };
}
function show(enabled = true) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return render(
        <QueryClientProvider client={client}>
            <TwitchPanel enabled={enabled} />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    listen.mockReset();
    listeners.clear();
    listen.mockImplementation(async (fn) => {
        listeners.add(fn);
        return () => {
            listeners.delete(fn);
        };
    });
    invoke.mockImplementation(async (cmd) =>
        cmd === "twitch_chat_feed"
            ? { events: [event("Hallo")] }
            : cmd === "twitch_event_feed"
              ? { events: [] }
              : { data: [] },
    );
});
it("uses the independent app chat feed, shows timestamps and keeps a rejected message draft", async () => {
    show();
    await screen.findByText("Hallo");
    expect(invoke.mock.calls.some(([cmd]) => cmd === "chat_history")).toBe(
        false,
    );
    expect(
        screen.getByText("Hallo").closest("div")?.querySelector("time"),
    ).toHaveAttribute("datetime", "2026-10-03T12:00:00Z");
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "twitch_action")
            throw new Error("Twitch rejected the message");
        return cmd === "twitch_chat_feed"
            ? { events: [event("Hallo")] }
            : cmd === "twitch_event_feed"
              ? { events: [] }
              : { data: [] };
    });
    await userEvent.type(
        screen.getByLabelText("Chatnachricht"),
        "Mein Entwurf",
    );
    await userEvent.click(screen.getByRole("button", { name: "Senden" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("rejected");
    expect(screen.getByLabelText("Chatnachricht")).toHaveValue("Mein Entwurf");
    expect(invoke).toHaveBeenCalledWith("twitch_action", {
        action: { action: "send_chat", message: "Mein Entwurf" },
    });
});
it("removes moderated messages through the shared snapshot and releases listeners on page exit", async () => {
    let events = [event("Erste", "first"), event("Zweite", "second")];
    invoke.mockImplementation(async (cmd) =>
        cmd === "twitch_chat_feed"
            ? { events }
            : cmd === "twitch_event_feed"
              ? { events: [] }
              : { data: [] },
    );
    const view = show();
    await screen.findByText("Zweite");
    events = [events[1]];
    act(() =>
        listeners.forEach((fn) =>
            fn({
                source: "twitch",
                type: "channel.chat.message_delete",
                data: { message_id: "first" },
            }),
        ),
    );
    await waitFor(() =>
        expect(screen.queryByText("Erste")).not.toBeInTheDocument(),
    );
    events = [];
    act(() =>
        listeners.forEach((fn) =>
            fn({
                source: "twitch",
                type: "channel.chat.clear_user_messages",
                data: { target_user_id: "u" },
            }),
        ),
    );
    await waitFor(() =>
        expect(screen.queryByText("Zweite")).not.toBeInTheDocument(),
    );
    view.unmount();
    expect(listeners.size).toBe(0);
});
it("shows history and listener failures instead of an apparently empty healthy chat", async () => {
    listen.mockRejectedValue(new Error("Chat listener unavailable"));
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "twitch_chat_feed")
            throw new Error("Chat snapshot unavailable");
        return cmd === "twitch_event_feed" ? { events: [] } : { data: [] };
    });
    show();
    await waitFor(() =>
        expect(
            screen
                .getAllByRole("alert")
                .map((el) => el.textContent)
                .join(" "),
        ).toContain("Chat listener unavailable"),
    );
    expect(
        screen
            .getAllByRole("alert")
            .map((el) => el.textContent)
            .join(" "),
    ).toContain("Chat snapshot unavailable");
});
it("keeps disconnected controls disabled and cannot moderate a message without an identifier", async () => {
    invoke.mockImplementation(async (cmd) =>
        cmd === "twitch_chat_feed"
            ? {
                  events: [
                      {
                          ...event("Ohne ID"),
                          data: { userName: "Alice", text: "Ohne ID" },
                      },
                  ],
              }
            : cmd === "twitch_event_feed"
              ? { events: [] }
              : { data: [] },
    );
    const view = show();
    await screen.findByText("Ohne ID");
    expect(screen.getByRole("button", { name: "Löschen" })).toBeDisabled();
    expect(
        screen.getByRole("button", { name: "10 Min. Timeout" }),
    ).toBeDisabled();
    view.unmount();
    show(false);
    await screen.findByText("Ohne ID");
    await userEvent.type(
        screen.getByLabelText("Chatnachricht"),
        "Offline-Entwurf",
    );
    expect(screen.getByRole("button", { name: "Senden" })).toBeDisabled();
    expect(
        screen.getByRole("button", { name: "Twitch-Webchat öffnen" }),
    ).toBeDisabled();
});

it("selects the chat login and routes quick moderation and confirmed clearing through the shared runtime", async () => {
    invoke.mockImplementation(async (cmd) =>
        cmd === "twitch_moderate"
            ? { applied: true, message: "Angewendet", warnings: [] }
            : cmd === "twitch_chat_feed"
              ? { events: [event("Hallo")] }
              : cmd === "twitch_event_feed"
                ? { events: [] }
                : { data: [] },
    );
    show();
    await screen.findByText("Hallo");
    await userEvent.click(screen.getByRole("button", { name: "Moderieren" }));
    expect(screen.getByLabelText("Moderationsbenutzer")).toHaveValue("alice");
    await userEvent.clear(screen.getByLabelText("Moderationsbenutzer"));
    await userEvent.type(
        screen.getByLabelText("Moderationsbenutzer"),
        "Entwurf",
    );
    await userEvent.click(screen.getByRole("button", { name: "Moderieren" }));
    expect(screen.getByLabelText("Moderationsbenutzer")).toHaveValue("alice");
    await userEvent.click(
        screen.getByRole("button", { name: "10 Min. Timeout" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_moderate", {
            action: {
                action: "timeout",
                user: "u",
                byId: true,
                minutes: 10,
                reason: "Chat-Moderation",
            },
        }),
    );
    await userEvent.click(screen.getByRole("button", { name: "Löschen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_moderate", {
            action: { action: "delete_message", messageId: "message" },
        }),
    );
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const before = invoke.mock.calls.filter(
        ([cmd]) => cmd === "twitch_moderate",
    ).length;
    await userEvent.click(screen.getByRole("button", { name: "Chat leeren" }));
    expect(
        invoke.mock.calls.filter(([cmd]) => cmd === "twitch_moderate"),
    ).toHaveLength(before);
    confirm.mockReturnValue(true);
    await userEvent.click(screen.getByRole("button", { name: "Chat leeren" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_moderate", {
            action: { action: "clear_chat" },
        }),
    );
    confirm.mockRestore();
});
