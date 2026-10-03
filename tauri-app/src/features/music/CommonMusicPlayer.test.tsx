import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { CommonMusicPlayer } from "./CommonMusicPlayer";
import { defaultAppSettings } from "../../lib/app-settings";
const invoke = vi.fn();
const listen = vi.fn(async (_handler: unknown) => () => {});
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenMusicPlayer: (handler: unknown) => listen(handler),
}));
const snapshot = {
    provider: "spotify",
    providerDisplayName: "Spotify",
    connected: true,
    bridgeRunning: false,
    isPlaying: true,
    title: "Song",
    artist: "Artist",
    album: "Album",
    coverUrl: "https://example.com/image",
    progressMs: 1500,
    durationMs: 5000,
    volumePercent: 42,
    supportsSeek: true,
    supportsVolume: true,
    statusText: "Spielt",
    error: null,
};
function show(provider = "spotify", pending = false) {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <CommonMusicPlayer provider={provider} changingProvider={pending} />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke
        .mockReset()
        .mockImplementation(async (cmd: string) =>
            cmd === "music_player_snapshot" ? snapshot : null,
        );
    listen.mockClear();
});
it("displays shared metadata and routes pause, seek and volume through the common commands", async () => {
    show();
    expect(await screen.findByText("Song")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Albumcover" })).toHaveAttribute(
        "src",
        snapshot.coverUrl,
    );
    fireEvent.click(screen.getByRole("button", { name: "Musik pausieren" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_player_action", {
            action: { action: "play_pause" },
        }),
    );
    const volume = screen.getByRole("slider", { name: "Musiklautstärke" });
    expect(volume).toHaveValue("42");
    fireEvent.change(volume, { target: { value: "25" } });
    fireEvent.keyUp(volume, { key: "ArrowLeft" });
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_player_action", {
            action: { action: "volume", percent: 25 },
        }),
    );
    const position = screen.getByRole("slider", { name: "Wiedergabeposition" });
    fireEvent.change(position, { target: { value: "3000" } });
    fireEvent.pointerUp(position);
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_player_action", {
            action: { action: "seek", positionMs: 3000 },
        }),
    );
});
it("shows YouTube status, hides unsupported controls and disconnects without logout", async () => {
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "music_player_snapshot"
            ? {
                  ...snapshot,
                  provider: "ytmusic",
                  providerDisplayName: "YouTube Music",
                  supportsSeek: false,
                  supportsVolume: false,
                  volumePercent: null,
              }
            : null,
    );
    show("ytmusic");
    expect(await screen.findByText("Song")).toBeInTheDocument();
    expect(
        screen.queryByRole("slider", { name: "Musiklautstärke" }),
    ).not.toBeInTheDocument();
    expect(
        screen.queryByRole("slider", { name: "Wiedergabeposition" }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Musik trennen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "music_player_disconnect",
            undefined,
        ),
    );
    expect(invoke.mock.calls.some(([cmd]) => cmd === "spotify_logout")).toBe(
        false,
    );
});
it("disables stale provider controls and keeps unknown volume unavailable", async () => {
    const view = show("ytmusic", true);
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_player_snapshot", undefined),
    );
    expect(screen.queryByText("Song")).not.toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Musik abspielen" }),
    ).toBeDisabled();
    view.unmount();
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "music_player_snapshot"
            ? {
                  ...snapshot,
                  volumePercent: null,
                  error: "Status nicht verfügbar",
              }
            : null,
    );
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent(
        "Status nicht verfügbar",
    );
    expect(
        screen.getByRole("slider", { name: "Musiklautstärke" }),
    ).toBeDisabled();
    expect(screen.getByText("Lautstärke unbekannt")).toBeInTheDocument();
});
it("connects the selected provider and displays operation failures", async () => {
    invoke.mockImplementation(async (cmd: string) => {
        if (cmd === "music_player_connect") throw Error("Port belegt");
        return cmd === "music_player_snapshot"
            ? {
                  ...snapshot,
                  connected: false,
                  title: "",
                  statusText: "Nicht verbunden",
              }
            : null;
    });
    show();
    const connect = screen.getByRole("button", { name: "Musik verbinden" });
    await waitFor(() => expect(connect).not.toBeDisabled());
    fireEvent.click(connect);
    expect(await screen.findByRole("alert")).toHaveTextContent("Port belegt");
});

it("shows a pending login and allows cancellation without starting another login", async () => {
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "music_player_snapshot"
            ? {
                  ...snapshot,
                  connected: false,
                  connecting: true,
                  title: "",
                  statusText: "Verbinde …",
              }
            : null,
    );
    show();
    expect(await screen.findByText("Verbinde …")).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Musik verbinden" }),
    ).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Musik trennen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "music_player_disconnect",
            undefined,
        ),
    );
});

it("disables cached playback controls after the snapshot query fails", async () => {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    client.setQueryData(["music-player"], snapshot);
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "music_player_snapshot")
            throw Error("Musikstatus fehlgeschlagen");
        return null;
    });
    render(
        <QueryClientProvider client={client}>
            <CommonMusicPlayer provider="spotify" />
        </QueryClientProvider>,
    );
    expect(
        await screen.findByText("Error: Musikstatus fehlgeschlagen"),
    ).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Musik abspielen" }),
    ).toBeDisabled();
    expect(
        screen.getByRole("button", { name: "Nächster Musiktitel" }),
    ).toBeDisabled();
    expect(screen.queryByText("Song")).not.toBeInTheDocument();
});

it("mounts Spotify quick playlists in the common player and hides them for YouTube Music", async () => {
    const settings = defaultAppSettings();
    settings.Spotify.FavoritePlaylistUris = ["spotify:playlist:studio"];
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "music_player_snapshot") return snapshot;
        if (cmd === "get_settings") return settings;
        if (cmd === "spotify_query")
            return args.query.query === "playback"
                ? { shuffle_state: false, repeat_state: "off" }
                : {
                      items: [
                          {
                              id: "studio",
                              uri: "spotify:playlist:studio",
                              name: "Studio",
                          },
                      ],
                  };
        return null;
    });
    const view = show();
    expect(
        await screen.findByRole("option", { name: "Studio" }),
    ).toBeInTheDocument();
    expect(
        screen.getByRole("checkbox", { name: "Zufallswiedergabe" }),
    ).toBeInTheDocument();
    view.unmount();
    show("ytmusic");
    expect(
        screen.queryByRole("combobox", { name: "Schnellplaylist" }),
    ).not.toBeInTheDocument();
});
