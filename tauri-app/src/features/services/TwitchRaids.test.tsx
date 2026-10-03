import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { TwitchRaids } from "./TwitchRaids";
const invoke = vi.fn();
const listen = vi.fn();
const listeners = new Set<() => void>();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenTwitchRaids: (fn: () => void) => listen(fn),
    FALLBACK_POLL_MS: 15000,
}));
let settings: { channels: string[]; selected: string; original: unknown };
const target = {
    id: "t",
    login: "target",
    displayName: "Target",
    isOnline: true,
    category: "Game",
    title: "Stream",
    viewerCount: 12,
    startedAt: "2026-10-03T10:00:00Z",
    channelUrl: "https://www.twitch.tv/target",
    profileImageUrl: "",
};
function show(enabled = true) {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: {
                        queries: { retry: false },
                        mutations: { retry: false },
                    },
                })
            }
        >
            <TwitchRaids enabled={enabled} />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    listen.mockReset();
    listeners.clear();
    settings = {
        channels: ["target"],
        selected: "target",
        original: { Twitch: { Future: true } },
    };
    listen.mockImplementation(async (fn) => {
        listeners.add(fn);
        return () => listeners.delete(fn);
    });
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "twitch_raid_settings") return structuredClone(settings);
        if (cmd === "twitch_raid_state")
            return {
                requestedTarget: null,
                requestedAt: null,
                lastError: null,
            };
        if (cmd === "twitch_raid_target")
            return args.login === "offline"
                ? { ...target, login: "offline", isOnline: false }
                : target;
        if (cmd === "twitch_raid_suggestions")
            return {
                suggestions: [
                    {
                        login: "offline",
                        displayName: "Offline",
                        isLive: false,
                        sourceLabel: "Gefolgt",
                    },
                ],
                warnings: ["Live-Abfrage: fehlender Scope"],
            };
        if (cmd === "select_twitch_raid_target") {
            settings = {
                ...settings,
                selected: args.login,
                channels: [args.login, ...settings.channels],
            };
            return settings;
        }
        if (cmd === "save_twitch_raid_settings") {
            settings = {
                ...settings,
                channels: args.channels,
                selected: args.selected,
            };
            return settings;
        }
        if (cmd === "start_twitch_raid")
            return {
                target,
                warnings: ["Raid gestartet; Chat-Bestätigung fehlgeschlagen"],
            };
        return null;
    });
    vi.spyOn(window, "confirm").mockReturnValue(true);
});
it("shows fresh target details, sends a login for preflight and reports partial success", async () => {
    const user = userEvent.setup();
    show();
    expect(await screen.findByText("Stream")).toBeInTheDocument();
    expect(screen.getByText("12 Zuschauer · Game")).toBeInTheDocument();
    expect(
        await screen.findByText("Live-Abfrage: fehlender Scope"),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Raid starten" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("start_twitch_raid", {
            login: "target",
        }),
    );
    expect(
        await screen.findByText(
            "Raid gestartet; Chat-Bestätigung fehlgeschlagen",
        ),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Raid abbrechen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("cancel_twitch_raid", undefined),
    );
});
it("selects an offline suggestion persistently and prevents starting until target is online", async () => {
    const user = userEvent.setup();
    show();
    await screen.findByText("Stream");
    await user.click(
        await screen.findByRole("button", {
            name: "Offline als Raid-Ziel wählen",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("select_twitch_raid_target", {
            login: "offline",
        }),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Raid starten" }),
        ).toBeDisabled(),
    );
});
it("keeps list drafts and originals on conflict and allows editing while disconnected", async () => {
    const user = userEvent.setup();
    show(false);
    const list = await screen.findByLabelText("Gespeicherte Raid-Kanäle");
    await waitFor(() => expect(list).toHaveValue("target"));
    await user.clear(list);
    await user.type(list, "new\nother");
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "save_twitch_raid_settings") throw Error("Konflikt");
        return settings;
    });
    await user.click(
        screen.getByRole("button", { name: "Raid-Liste speichern" }),
    );
    expect(await screen.findByText(/Konflikt/)).toBeInTheDocument();
    expect(list).toHaveValue("new\nother");
    expect(invoke).toHaveBeenCalledWith("save_twitch_raid_settings", {
        channels: ["new", "other"],
        selected: "target",
        original: { Twitch: { Future: true } },
    });
    expect(screen.getByRole("button", { name: "Raid starten" })).toBeDisabled();
});
it("reports denied cancellation and keeps the requested raid visible", async () => {
    invoke.mockImplementation(async (cmd) =>
        cmd === "twitch_raid_settings"
            ? settings
            : cmd === "twitch_raid_state"
              ? {
                    requestedTarget: "target",
                    requestedAt: "now",
                    lastError: null,
                }
              : cmd === "cancel_twitch_raid"
                ? Promise.reject(Error("403: Missing scope"))
                : cmd === "twitch_raid_target"
                  ? target
                  : { suggestions: [], warnings: [] },
    );
    const user = userEvent.setup();
    show();
    await screen.findByText(/Start angefordert: target/);
    await user.click(screen.getByRole("button", { name: "Raid abbrechen" }));
    expect(await screen.findByText(/403: Missing scope/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Raid starten" })).toBeDisabled();
});
it("refreshes shared selection on events and cleans up late subscriptions", async () => {
    const view = show();
    await screen.findByText("Stream");
    settings = { ...settings, selected: "offline" };
    act(() => listeners.forEach((fn) => fn()));
    await waitFor(() =>
        expect(screen.getByLabelText("Ausgewähltes Raid-Ziel")).toHaveValue(
            "offline",
        ),
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

it("does not replace a new search with a late manual refresh response", async () => {
    let resolve!: (value: unknown) => void;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "twitch_raid_suggestions" && args.force)
            return new Promise((r) => {
                resolve = r;
            });
        if (cmd === "twitch_raid_suggestions")
            return {
                suggestions: [
                    {
                        login: args.query || "initial",
                        displayName: args.query || "Initial",
                        isLive: false,
                        sourceLabel: "Suche",
                    },
                ],
                warnings: [],
            };
        if (cmd === "twitch_raid_settings") return settings;
        if (cmd === "twitch_raid_state") return { requestedTarget: null };
        return target;
    });
    const user = userEvent.setup();
    show();
    await screen.findByRole("button", { name: "Initial als Raid-Ziel wählen" });
    await user.click(
        screen.getByRole("button", { name: "Vorschläge aktualisieren" }),
    );
    await user.type(screen.getByLabelText("Raid-Kanäle suchen"), "newquery");
    await screen.findByRole("button", {
        name: "newquery als Raid-Ziel wählen",
    });
    await act(async () =>
        resolve({
            suggestions: [
                {
                    login: "old",
                    displayName: "Old",
                    isLive: true,
                    sourceLabel: "Live",
                },
            ],
            warnings: [],
        }),
    );
    expect(
        screen.getByRole("button", { name: "newquery als Raid-Ziel wählen" }),
    ).toBeInTheDocument();
    expect(
        screen.queryByRole("button", { name: "Old als Raid-Ziel wählen" }),
    ).not.toBeInTheDocument();
});
