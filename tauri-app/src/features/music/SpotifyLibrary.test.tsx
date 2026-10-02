import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    render,
    screen,
    fireEvent,
    waitFor,
    within,
} from "@testing-library/react";
import { beforeEach, it, expect, vi } from "vitest";
import { defaultAppSettings } from "../../lib/app-settings";
import { SpotifyLibrary } from "./SpotifyLibrary";
const invoke = vi.fn();
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
const song = {
    id: "song",
    uri: "spotify:track:song",
    name: "Song",
    type: "track",
    artists: [{ name: "Artist" }],
    album: { name: "Album" },
    duration_ms: 123000,
};

it("checks saved status in batches of forty and persists playlist shuffle settings", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "spotify_query" && args.query.query === "saved")
            return { items: [], next: null };
        if (cmd === "spotify_query" && args.query.query === "playlist_tracks")
            return {
                items: Array.from({ length: 50 }, (_, index) => ({
                    item: { ...song, id: `song${index}` },
                })),
                next: null,
                limit: 50,
            };
        if (cmd === "spotify_query" && args.query.query === "saved_status")
            return args.query.ids.map(() => false);
        return base(cmd, args);
    });
    show();
    await screen.findByText("Studio");
    fireEvent.click(
        screen.getByRole("checkbox", {
            name: "Playlists mit Zufallswiedergabe starten",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "save_settings",
            expect.objectContaining({
                settings: expect.objectContaining({
                    Spotify: expect.objectContaining({
                        ShuffleSelectedPlaylist: true,
                    }),
                }),
            }),
        ),
    );
    fireEvent.click(
        screen.getByRole("button", { name: "Titel anzeigen: Studio" }),
    );
    await waitFor(() =>
        expect(
            invoke.mock.calls
                .filter(
                    ([cmd, args]) =>
                        cmd === "spotify_query" &&
                        args.query.query === "saved_status",
                )
                .map(([, args]) => args.query.ids.length),
        ).toEqual([40, 10]),
    );
    expect(
        screen.getAllByRole("button", { name: "Als Favorit merken" }),
    ).toHaveLength(50);
});
beforeEach(() => {
    invoke.mockReset();
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "get_settings") return defaultAppSettings();
        if (cmd === "spotify_query") {
            if (args.query.query === "all_playlists")
                return {
                    items: [
                        {
                            id: "list",
                            uri: "spotify:playlist:list",
                            name: "Studio",
                            owner: { display_name: "Owner" },
                        },
                    ],
                };
            if (args.query.query === "saved_status")
                return args.query.ids.map((id: string) => id === "song");
            return { items: [], next: null, limit: 50 };
        }
        return null;
    });
});
function show() {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <SpotifyLibrary />
        </QueryClientProvider>,
    );
}
it("reads new and legacy playlist items, skips missing/episode entries, and pages by server limit", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) =>
        cmd === "spotify_query" && args.query.query === "playlist_tracks"
            ? {
                  items: [
                      { item: song },
                      {
                          item: null,
                          track: { ...song, id: "legacy", name: "Legacy" },
                      },
                      { item: null },
                      { item: { ...song, type: "episode", name: "Podcast" } },
                  ],
                  next: args.offset === 0 ? "next" : null,
                  limit: 50,
              }
            : base(cmd, args),
    );
    show();
    await screen.findByText("Studio");
    fireEvent.click(
        screen.getByRole("button", { name: "Titel anzeigen: Studio" }),
    );
    expect(await screen.findByText("Song")).toBeInTheDocument();
    expect(screen.getByText("Legacy")).toBeInTheDocument();
    expect(screen.queryByText("Podcast")).not.toBeInTheDocument();
    const row = screen.getByText("Song").closest("li")!;
    fireEvent.click(
        await within(row).findByRole("button", { name: "Favorit entfernen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("spotify_action", {
            action: { action: "remove_saved_track", id: "song" },
        }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Nächste Seite" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("spotify_query", {
            query: { query: "playlist_tracks", id: "list" },
            offset: 50,
        }),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("button", { name: "Nächste Seite" }),
        ).toBeDisabled(),
    );
});
it("uses ten-item search pagination and shows rejected writes", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "spotify_query" && args.query.query === "search")
            return {
                tracks: {
                    items: [song],
                    limit: 10,
                    next: args.offset === 0 ? "next" : null,
                },
            };
        if (cmd === "spotify_action") throw new Error("Not permitted");
        return base(cmd, args);
    });
    show();
    await screen.findByText("Studio");
    fireEvent.change(screen.getByLabelText("Titel suchen"), {
        target: { value: "  Song  " },
    });
    fireEvent.click(screen.getByRole("button", { name: "Suchen" }));
    await screen.findByText("Song");
    fireEvent.click(screen.getByRole("button", { name: "Einreihen" }));
    expect(await screen.findByText(/Not permitted/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Nächste Seite" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("spotify_query", {
            query: { query: "search", text: "Song" },
            offset: 10,
        }),
    );
});
it("persists playlist favorites and exposes recent/favorite quick play", async () => {
    const settings = defaultAppSettings();
    Object.assign(settings.Spotify, {
        RecentPlaylistUris: ["spotify:playlist:list"],
    });
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) =>
        cmd === "get_settings" ? settings : base(cmd, args),
    );
    show();
    await screen.findByText("Studio");
    fireEvent.click(
        screen.getByRole("button", {
            name: "Playlist als Favorit merken: Studio",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("set_spotify_playlist_favorite", {
            uri: "spotify:playlist:list",
            favorite: true,
        }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Schnellzugriff" }));
    fireEvent.click(screen.getByRole("button", { name: "Abspielen: Studio" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("spotify_action", {
            action: { action: "play_playlist", uri: "spotify:playlist:list" },
        }),
    );
    fireEvent.change(screen.getByLabelText("Playlists filtern"), {
        target: { value: "Owner" },
    });
    expect(screen.getByText("Studio")).toBeInTheDocument();
});
it("shows queue current track and recently-played timestamps without pagination", async () => {
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "spotify_query" && args.query.query === "queue")
            return {
                currently_playing: song,
                queue: [{ ...song, name: "Queued" }],
            };
        if (cmd === "spotify_query" && args.query.query === "recent")
            return {
                items: [{ track: song, played_at: "2026-01-01T12:30:00Z" }],
            };
        return base(cmd, args);
    });
    show();
    await screen.findByText("Studio");
    fireEvent.click(screen.getByRole("button", { name: "Warteschlange" }));
    expect(await screen.findByText(/Aktuell: Song/)).toBeInTheDocument();
    expect(screen.getByText("Queued")).toBeInTheDocument();
    expect(
        screen.queryByRole("button", { name: "Nächste Seite" }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Zuletzt gehört" }));
    await screen.findByText("Song");
    expect(screen.getByText(/Gespielt:/)).toBeInTheDocument();
});
