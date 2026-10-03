import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    act,
    fireEvent,
    render,
    screen,
    waitFor,
} from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { defaultAppSettings } from "../../lib/app-settings";
import { SpotifyQuickControls } from "./SpotifyQuickControls";
const invoke = vi.fn();
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
let settings = defaultAppSettings();
let playback: unknown = { shuffle_state: true, repeat_state: "track" };
const catalog = {
    items: [
        { id: "a", uri: "spotify:playlist:a", name: "Studio" },
        { id: "b", uri: "spotify:playlist:b", name: "Ende" },
        { id: "c", uri: "spotify:playlist:c", name: "Andere" },
    ],
};
beforeEach(() => {
    settings = defaultAppSettings();
    settings.Spotify.FavoritePlaylistUris = [
        "spotify:playlist:A",
        "spotify:playlist:gone",
    ];
    settings.Spotify.RecentPlaylistUris = [
        "spotify:playlist:a",
        "spotify:playlist:b",
    ];
    playback = { shuffle_state: true, repeat_state: "track" };
    invoke.mockReset().mockImplementation(async (cmd, args) => {
        if (cmd === "get_settings") return structuredClone(settings);
        if (cmd === "spotify_query")
            return args.query.query === "playback" ? playback : catalog;
        if (cmd === "spotify_action") return null;
    });
});
function show(enabled = true) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    const view = render(
        <QueryClientProvider client={client}>
            <SpotifyQuickControls enabled={enabled} />
        </QueryClientProvider>,
    );
    return { client, ...view };
}
it("uses actual shuffle/repeat and favorite-before-recent quick playlists through existing commands", async () => {
    show();
    const quick = await screen.findByRole("combobox", {
        name: "Schnellplaylist",
    });
    await waitFor(() => expect(quick).toHaveValue("spotify:playlist:a"));
    expect(
        screen
            .getAllByRole("option")
            .filter((o) =>
                String((o as HTMLOptionElement).value).startsWith("spotify:"),
            )
            .map((o) => o.textContent),
    ).toEqual(["Studio", "Ende"]);
    const shuffle = screen.getByRole("checkbox", { name: "Zufallswiedergabe" });
    expect(shuffle).toBeChecked();
    expect(screen.getByRole("combobox", { name: "Wiederholung" })).toHaveValue(
        "track",
    );
    fireEvent.click(shuffle);
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("spotify_action", {
            action: { action: "shuffle", enabled: false },
        }),
    );
    await waitFor(() => expect(shuffle).not.toBeDisabled());
    fireEvent.change(screen.getByRole("combobox", { name: "Wiederholung" }), {
        target: { value: "context" },
    });
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("spotify_action", {
            action: { action: "repeat", mode: "context" },
        }),
    );
    await waitFor(() => expect(quick).not.toBeDisabled());
    fireEvent.change(quick, { target: { value: "spotify:playlist:b" } });
    fireEvent.click(
        screen.getByRole("button", { name: "Schnellplaylist starten" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("spotify_action", {
            action: { action: "play_playlist", uri: "spotify:playlist:b" },
        }),
    );
    expect(
        await screen.findByText("Playliststart angefordert."),
    ).toBeInTheDocument();
});
it("keeps unknown and failed playback states unavailable and retries without pretending cached values are current", async () => {
    playback = {};
    const { client } = show();
    expect(
        await screen.findByText("Zufallswiedergabe unbekannt"),
    ).toBeInTheDocument();
    expect(
        screen.getByRole("checkbox", { name: "Zufallswiedergabe" }),
    ).toBeDisabled();
    expect(screen.getByRole("combobox", { name: "Wiederholung" })).toHaveValue(
        "",
    );
    playback = { shuffle_state: false, repeat_state: "off" };
    await waitFor(() =>
        expect(
            screen.getByRole("button", {
                name: "Spotify-Optionen aktualisieren",
            }),
        ).not.toBeDisabled(),
    );
    fireEvent.click(
        screen.getByRole("button", { name: "Spotify-Optionen aktualisieren" }),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("checkbox", { name: "Zufallswiedergabe" }),
        ).not.toBeDisabled(),
    );
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "spotify_query" && args.query.query === "playback")
            throw Error("Spotify API 403");
        return base(cmd, args);
    });
    await act(() =>
        client.invalidateQueries({ queryKey: ["spotify-playback"] }),
    );
    expect(await screen.findByText("Spotify API 403")).toBeInTheDocument();
    expect(
        screen.getByRole("checkbox", { name: "Zufallswiedergabe" }),
    ).toBeDisabled();
    expect(screen.getByRole("combobox", { name: "Wiederholung" })).toHaveValue(
        "",
    );
});
it("preserves quick selection on background refresh and shows a rejected playlist start", async () => {
    const { client } = show();
    const quick = await screen.findByRole("combobox", {
        name: "Schnellplaylist",
    });
    await waitFor(() => expect(quick).not.toBeDisabled());
    fireEvent.change(quick, { target: { value: "spotify:playlist:b" } });
    settings.Spotify.FavoritePlaylistUris = [
        "spotify:playlist:c",
        "spotify:playlist:a",
    ];
    await act(() => client.invalidateQueries({ queryKey: ["settings"] }));
    expect(quick).toHaveValue("spotify:playlist:b");
    const base = invoke.getMockImplementation()!;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "spotify_action")
            throw Error(
                "Playlist gestartet; Verlauf konnte nicht gespeichert werden",
            );
        return base(cmd, args);
    });
    fireEvent.click(
        screen.getByRole("button", { name: "Schnellplaylist starten" }),
    );
    expect(
        await screen.findByText(
            "Playlist gestartet; Verlauf konnte nicht gespeichert werden",
        ),
    ).toBeInTheDocument();
    expect(
        screen.queryByText("Playliststart angefordert."),
    ).not.toBeInTheDocument();
    expect(quick).toHaveValue("spotify:playlist:b");
});
it("retains the initially selected playlist when favorites are reordered", async () => {
    const { client } = show();
    const quick = await screen.findByRole("combobox", {
        name: "Schnellplaylist",
    });
    await waitFor(() => expect(quick).toHaveValue("spotify:playlist:a"));
    settings.Spotify.FavoritePlaylistUris = [
        "spotify:playlist:c",
        "spotify:playlist:a",
    ];
    await act(() => client.invalidateQueries({ queryKey: ["settings"] }));
    await screen.findByRole("option", { name: "Andere" });
    expect(quick).toHaveValue("spotify:playlist:a");
});
it("does not request Spotify data or send actions while disconnected", async () => {
    show(false);
    expect(
        screen.getByRole("button", { name: "Schnellplaylist starten" }),
    ).toBeDisabled();
    expect(
        screen.getByRole("checkbox", { name: "Zufallswiedergabe" }),
    ).toBeDisabled();
    expect(invoke).not.toHaveBeenCalled();
});
