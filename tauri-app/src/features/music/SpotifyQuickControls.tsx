import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "../../components/ui/button";
import { queryKeys, tauriInvoke } from "../../lib/api";
import type { AppSettings } from "../../lib/app-settings";
import type { SpotifyAction } from "../../lib/command-contract";
import {
    quickPlaylistChoices,
    spotifyPlaylistsKey,
    type SpotifyPlaylist,
} from "./spotify-catalog";

type Playback = { shuffle_state?: boolean; repeat_state?: string };
export function SpotifyQuickControls({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
        enabled,
    });
    const playlists = useQuery({
        queryKey: spotifyPlaylistsKey,
        queryFn: () =>
            tauriInvoke<{ items: SpotifyPlaylist[] }>("spotify_query", {
                query: { query: "all_playlists" },
            }),
        enabled,
    });
    const playback = useQuery({
        queryKey: ["spotify-playback"],
        queryFn: () =>
            tauriInvoke<Playback | null>("spotify_query", {
                query: { query: "playback" },
            }),
        enabled,
        refetchInterval: 5000,
    });
    const [selection, setSelection] = useState("");
    const [message, setMessage] = useState("");
    const choices =
        enabled && !settings.isError && !playlists.isError
            ? quickPlaylistChoices(playlists.data?.items ?? [], settings.data)
            : [];
    const selected =
        choices.find(
            (item) => item.uri.toLowerCase() === selection.toLowerCase(),
        )?.uri ??
        choices[0]?.uri ??
        "";
    useEffect(() => {
        if (selected && selected !== selection) setSelection(selected);
    }, [selected, selection]);
    const current = enabled && !playback.isError ? playback.data : null;
    const shuffleKnown = typeof current?.shuffle_state === "boolean";
    const repeatKnown = ["off", "context", "track"].includes(
        current?.repeat_state ?? "",
    );
    const action = useMutation({
        mutationFn: (request: SpotifyAction) =>
            tauriInvoke("spotify_action", { action: request }),
        onSuccess: (_, request) =>
            setMessage(
                request.action === "play_playlist"
                    ? "Playliststart angefordert."
                    : "Wiedergabeoption angefordert.",
            ),
        // Even a partial playlist failure may have changed playback/history.
        onSettled: async () => {
            await Promise.all(
                [
                    queryKeys.settings,
                    queryKeys.musicPlayer,
                    queryKeys.nowPlaying,
                    ["spotify-playback"],
                    ["spotify-devices"],
                ].map((queryKey) => client.invalidateQueries({ queryKey })),
            );
        },
    });
    const busy = !enabled || action.isPending;
    const error =
        action.error ??
        (enabled
            ? (playback.error ?? playlists.error ?? settings.error)
            : null);
    function perform(request: SpotifyAction) {
        setMessage("");
        action.mutate(request);
    }
    return (
        <div className="space-y-3 border-t border-white/10 pt-4">
            <div className="flex flex-wrap items-end gap-2">
                <label className="flex min-w-48 flex-1 flex-col gap-1">
                    Schnellplaylist
                    <select
                        aria-label="Schnellplaylist"
                        className="bg-panel rounded p-2"
                        disabled={busy || !selected}
                        value={selected}
                        onChange={(event) => setSelection(event.target.value)}
                    >
                        {!selected && (
                            <option value="">
                                Keine Schnellplaylist verfügbar
                            </option>
                        )}
                        {choices.map((item) => (
                            <option key={item.uri} value={item.uri}>
                                {item.name}
                            </option>
                        ))}
                    </select>
                </label>
                <Button
                    disabled={busy || !selected}
                    onClick={() =>
                        perform({ action: "play_playlist", uri: selected })
                    }
                >
                    Schnellplaylist starten
                </Button>
            </div>
            <div className="flex flex-wrap items-center gap-4">
                <label className="flex items-center gap-2">
                    <input
                        aria-label="Zufallswiedergabe"
                        type="checkbox"
                        checked={shuffleKnown ? current!.shuffle_state : false}
                        disabled={busy || !shuffleKnown}
                        onChange={(event) =>
                            perform({
                                action: "shuffle",
                                enabled: event.target.checked,
                            })
                        }
                    />
                    {shuffleKnown
                        ? "Zufallswiedergabe"
                        : "Zufallswiedergabe unbekannt"}
                </label>
                <label className="flex items-center gap-2">
                    Wiederholung
                    <select
                        aria-label="Wiederholung"
                        className="bg-panel rounded p-2"
                        value={repeatKnown ? current!.repeat_state : ""}
                        disabled={busy || !repeatKnown}
                        onChange={(event) =>
                            perform({
                                action: "repeat",
                                mode: event.target.value,
                            })
                        }
                    >
                        <option value="" disabled>
                            Unbekannt
                        </option>
                        <option value="off">Aus</option>
                        <option value="context">Playlist</option>
                        <option value="track">Titel</option>
                    </select>
                </label>
                <Button
                    disabled={
                        busy || playback.isFetching || playlists.isFetching
                    }
                    onClick={() => {
                        action.reset();
                        setMessage("");
                        void Promise.all([
                            playback.refetch(),
                            playlists.refetch(),
                            settings.refetch(),
                        ]);
                    }}
                >
                    Spotify-Optionen aktualisieren
                </Button>
            </div>
            {error && (
                <p role="alert" className="text-red-400">
                    {error instanceof Error ? error.message : String(error)}
                </p>
            )}
            {message && <p role="status">{message}</p>}
        </div>
    );
}
