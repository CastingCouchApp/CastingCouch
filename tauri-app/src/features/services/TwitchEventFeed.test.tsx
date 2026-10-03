import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { TwitchEventFeed } from "./TwitchEventFeed";

const invoke = vi.fn();
const listen = vi.fn();
const listeners = new Set<(event: Record<string, unknown>) => void>();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string) => invoke(cmd),
    listenTwitchEvents: (fn: (event: Record<string, unknown>) => void) =>
        listen(fn),
    FALLBACK_POLL_MS: 15000,
}));
function event(summary: string, type = "channel.follow") {
    return {
        source: "twitch",
        type,
        at: "2026-10-03T12:00:00Z",
        summary,
        data: {},
    };
}
function show() {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return render(
        <QueryClientProvider client={client}>
            <TwitchEventFeed />
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
});
it("renders the native receipt order, local times, warnings and unknown events as text", async () => {
    invoke.mockResolvedValue({
        events: [
            event("Alice folgt dem Kanal."),
            event("Fehlende Berechtigung", "subscription.warning"),
            event("<script>future()</script>", "channel.future.event"),
        ],
    });
    const view = show();
    const log = await screen.findByRole("list", { name: "Twitch-Ereignisse" });
    await screen.findByText("<script>future()</script>");
    const items = within(log).getAllByRole("listitem");
    expect(items.map((item) => item.textContent)).toEqual([
        expect.stringContaining("Alice folgt dem Kanal."),
        expect.stringContaining("Fehlende Berechtigung"),
        expect.stringContaining("<script>future()</script>"),
    ]);
    expect(items[0].querySelector("time")).toHaveAttribute(
        "datetime",
        "2026-10-03T12:00:00Z",
    );
    expect(items[0].querySelector("time")?.textContent).toMatch(
        /^\d{2}:\d{2}:\d{2}$/,
    );
    expect(log.querySelector("script")).toBeNull();
    expect(invoke).toHaveBeenCalledWith("twitch_event_feed");
    view.unmount();
    expect(listeners.size).toBe(0);
});
it("coalesces event bursts, recovers missed events from the snapshot and survives page remount", async () => {
    let events = [event("Vor Seitenwechsel")];
    invoke.mockImplementation(async () => ({ events }));
    const view = show();
    await screen.findByText("Vor Seitenwechsel");
    await waitFor(() => expect(listeners.size).toBe(1));
    const count = invoke.mock.calls.length;
    events = [
        ...events,
        event("Verpasster Raid", "channel.raid"),
        event("Chat bereinigt", "channel.chat.clear"),
    ];
    act(() => {
        for (let i = 0; i < 20; i++)
            listeners.forEach((fn) =>
                fn(event("Chat bereinigt", "channel.chat.clear")),
            );
    });
    await screen.findByText("Verpasster Raid");
    expect(screen.getAllByText("Chat bereinigt")).toHaveLength(1);
    expect(invoke.mock.calls.length).toBe(count + 1);
    view.unmount();
    show();
    await screen.findByText("Vor Seitenwechsel");
    await screen.findByText("Verpasster Raid");
});
it("exposes snapshot and subscription failures and retries without erasing a usable feed", async () => {
    listen.mockRejectedValueOnce(new Error("Listener unavailable"));
    invoke.mockResolvedValue({ events: [event("Erhalten")] });
    show();
    await screen.findByText("Erhalten");
    expect(await screen.findByRole("alert")).toHaveTextContent(
        "Listener unavailable",
    );
    invoke.mockRejectedValue(new Error("Native command unavailable"));
    await userEvent.click(
        screen.getByRole("button", { name: "Ereignisse aktualisieren" }),
    );
    await waitFor(() =>
        expect(screen.getByRole("alert")).toHaveTextContent(
            "Native command unavailable",
        ),
    );
    expect(screen.getByText("Erhalten")).toBeInTheDocument();
    invoke.mockResolvedValue({ events: [event("Wiederhergestellt")] });
    await userEvent.click(
        screen.getByRole("button", { name: "Ereignisse aktualisieren" }),
    );
    await screen.findByText("Wiederhergestellt");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(listeners.size).toBe(1);
});
it("discards an older in-flight snapshot after a live event and releases a late listener", async () => {
    let resolveOld!: (value: unknown) => void;
    let resolveListener!: (value: () => void) => void;
    invoke.mockImplementationOnce(
        () =>
            new Promise((resolve) => {
                resolveOld = resolve;
            }),
    );
    invoke.mockResolvedValue({ events: [event("Aktueller Stand")] });
    const view = show();
    await waitFor(() => expect(listeners.size).toBe(1));
    act(() => listeners.forEach((fn) => fn(event("Aktueller Stand"))));
    await screen.findByText("Aktueller Stand");
    await act(async () => {
        resolveOld({ events: [event("Veraltet")] });
    });
    expect(screen.queryByText("Veraltet")).not.toBeInTheDocument();
    view.unmount();
    const unlisten = vi.fn();
    listen.mockImplementationOnce(
        () =>
            new Promise((resolve) => {
                resolveListener = resolve;
            }),
    );
    const second = show();
    second.unmount();
    await act(async () => {
        resolveListener(unlisten);
    });
    expect(unlisten).toHaveBeenCalledOnce();
});
it("shows the empty state only after a successful snapshot and handles missing summaries or dates", async () => {
    invoke.mockRejectedValue(new Error("Read failed"));
    const view = show();
    await screen.findByRole("alert");
    expect(
        screen.queryByText("Noch keine Twitch-Ereignisse empfangen."),
    ).not.toBeInTheDocument();
    invoke.mockResolvedValue({ events: [] });
    await userEvent.click(
        screen.getByRole("button", { name: "Ereignisse aktualisieren" }),
    );
    await screen.findByText("Noch keine Twitch-Ereignisse empfangen.");
    invoke.mockResolvedValue({
        events: [{ ...event("", "channel.future"), at: "invalid" }],
    });
    await userEvent.click(
        screen.getByRole("button", { name: "Ereignisse aktualisieren" }),
    );
    await screen.findByText("channel.future");
    expect(screen.getByText("—")).toBeInTheDocument();
    view.unmount();
});
it("does not refetch the event feed for chat message bursts or unrelated app events", async () => {
    invoke.mockResolvedValue({ events: [event("Feed bleibt erhalten")] });
    show();
    await screen.findByText("Feed bleibt erhalten");
    await waitFor(() => expect(listeners.size).toBe(1));
    const count = invoke.mock.calls.length;
    act(() => {
        for (let index = 0; index < 30; index++) {
            listeners.forEach((fn) =>
                fn(event("Chat", "channel.chat.message")),
            );
            listeners.forEach((fn) =>
                fn({ ...event("Musik", "app.music.track"), source: "app" }),
            );
        }
    });
    await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 200));
    });
    expect(invoke.mock.calls.length).toBe(count);
    expect(screen.getByText("Feed bleibt erhalten")).toBeInTheDocument();
});
