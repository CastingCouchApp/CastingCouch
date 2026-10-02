import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { queryKeys, tauriInvoke } from "../../lib/api";
import { cloneSettings, type AppSettings } from "../../lib/app-settings";
import type { SpotifyAction, SpotifyQuery } from "../../lib/command-contract";

type Track = {
    id?: string | null;
    uri: string;
    name: string;
    type?: string;
    artists?: { name: string }[];
    album?: { name: string; images?: { url: string }[] };
    duration_ms?: number;
    is_playable?: boolean;
    is_local?: boolean;
    restrictions?: { reason: string };
};
type Playlist = {
    id: string;
    uri: string;
    name: string;
    owner?: { display_name?: string; id?: string };
    images?: { url: string }[];
    items?: { total: number };
    tracks?: { total: number };
};
type Entry = Track & {
    item?: Track | null;
    track?: Track | null;
    played_at?: string;
};
type Page = {
    items?: Entry[];
    tracks?: Page;
    queue?: Track[];
    currently_playing?: Track | null;
    limit?: number;
    offset?: number;
    next?: string | null;
    total?: number;
};
const libraryKey = ["spotify-library"] as const;
const playlistsKey = ["spotify-playlists"] as const;
function stringList(value: unknown): string[] {
    return Array.isArray(value)
        ? value.filter((v): v is string => typeof v === "string")
        : [];
}
function trackFrom(entry: Entry | null): Track | null {
    if (!entry) return null;
    const track =
        entry.item ??
        entry.track ??
        ("item" in entry || "track" in entry ? null : entry);
    return track &&
        (!track.type || track.type === "track") &&
        typeof track.name === "string" &&
        typeof track.uri === "string"
        ? track
        : null;
}

export function SpotifyLibrary() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const playlists = useQuery({
        queryKey: playlistsKey,
        queryFn: () =>
            tauriInvoke<{ items: Playlist[] }>("spotify_query", {
                query: { query: "all_playlists" },
            }),
    });
    const [query, setQuery] = useState<SpotifyQuery>({
        query: "all_playlists",
    });
    const [offset, setOffset] = useState(0);
    const [search, setSearch] = useState("");
    const [filter, setFilter] = useState("");
    const [quick, setQuick] = useState(false);
    const [playlistName, setPlaylistName] = useState("");
    const [message, setMessage] = useState("");
    const catalog = useQuery({
        queryKey: [...libraryKey, query, offset],
        queryFn: () => tauriInvoke<Page>("spotify_query", { query, offset }),
        enabled: query.query !== "all_playlists",
    });
    const page = catalog.data?.tracks ?? catalog.data;
    const entries = page?.items ?? page?.queue ?? [];
    const rows = entries.flatMap((entry, index) => {
        const track = trackFrom(entry);
        return track
            ? [{ track, index, playedAt: (entry as Entry)?.played_at }]
            : [];
    });
    const ids = [
        ...new Set(
            rows.map((row) => row.track.id).filter((id): id is string => !!id),
        ),
    ];
    const saved = useQuery({
        queryKey: ["spotify-saved-status", ids],
        enabled:
            query.query !== "all_playlists" &&
            query.query !== "saved" &&
            ids.length > 0,
        queryFn: async () => {
            const result: Record<string, boolean> = {};
            for (let start = 0; start < ids.length; start += 40) {
                const batch = ids.slice(start, start + 40);
                const status = await tauriInvoke<boolean[]>("spotify_query", {
                    query: { query: "saved_status", ids: batch },
                });
                if (
                    !Array.isArray(status) ||
                    status.length !== batch.length ||
                    status.some((flag) => typeof flag !== "boolean")
                )
                    throw new Error(
                        "Spotify-Favoritenprüfung lieferte eine ungültige Antwort.",
                    );
                batch.forEach((id, index) => (result[id] = status[index]));
            }
            return result;
        },
    });
    async function refreshPlayback() {
        await Promise.all([
            client.invalidateQueries({ queryKey: libraryKey }),
            client.invalidateQueries({ queryKey: ["spotify-saved-status"] }),
            client.invalidateQueries({ queryKey: ["spotify-playback"] }),
            client.invalidateQueries({ queryKey: queryKeys.nowPlaying }),
            client.invalidateQueries({ queryKey: queryKeys.settings }),
        ]);
    }
    const action = useMutation({
        mutationFn: (request: SpotifyAction) =>
            tauriInvoke("spotify_action", { action: request }),
        onSuccess: async (_, request) => {
            await refreshPlayback();
            setMessage(
                request.action === "queue"
                    ? "Titel eingereiht."
                    : request.action === "save_track" ||
                        request.action === "remove_saved_track"
                      ? "Favoriten aktualisiert."
                      : "Wiedergabe gestartet.",
            );
        },
    });
    const favorite = useMutation({
        mutationFn: ({ uri, favorite }: { uri: string; favorite: boolean }) =>
            tauriInvoke("set_spotify_playlist_favorite", { uri, favorite }),
        onSuccess: async () => {
            await client.invalidateQueries({ queryKey: queryKeys.settings });
            setMessage("Playlist-Favoriten gespeichert.");
        },
    });
    const preferences = useMutation({
        mutationFn: async (shuffle: boolean) => {
            if (!settings.data) throw new Error("Einstellungen fehlen.");
            const next = cloneSettings(settings.data);
            next.Spotify.ShuffleSelectedPlaylist = shuffle;
            return tauriInvoke<{ warnings: string[] }>("save_settings", {
                original: settings.data,
                settings: next,
            });
        },
        onSuccess: async () => {
            await client.invalidateQueries({ queryKey: queryKeys.settings });
        },
    });
    function select(next: SpotifyQuery, isQuick = false) {
        setQuery(next);
        setOffset(0);
        setQuick(isQuick);
        setMessage("");
    }
    function perform(request: SpotifyAction) {
        setMessage("");
        action.mutate(request);
    }
    const favorites = stringList(settings.data?.Spotify.FavoritePlaylistUris);
    const recent = stringList(settings.data?.Spotify.RecentPlaylistUris);
    const all = playlists.data?.items ?? [];
    const quickUris = [
        ...new Set([...favorites, ...recent].map((uri) => uri.toLowerCase())),
    ];
    const available = quick
        ? quickUris.flatMap((uri) =>
              all
                  .filter((playlist) => playlist.uri?.toLowerCase() === uri)
                  .slice(0, 1),
          )
        : all;
    const shown = available.filter((playlist) =>
        `${playlist.name} ${playlist.owner?.display_name ?? playlist.owner?.id ?? ""}`
            .toLowerCase()
            .includes(filter.trim().toLowerCase()),
    );
    const paged = ["saved", "playlist_tracks", "search"].includes(query.query);
    const limit =
        typeof page?.limit === "number" && page.limit > 0
            ? page.limit
            : query.query === "search"
              ? 10
              : 50;
    const more =
        typeof page?.next === "string" &&
        !!page.next.trim() &&
        (query.query !== "search" || offset + limit <= 1000);
    const error =
        preferences.error ??
        action.error ??
        favorite.error ??
        (query.query === "all_playlists" ? playlists.error : catalog.error) ??
        saved.error ??
        settings.error;
    const busy =
        action.isPending || favorite.isPending || preferences.isPending;
    return (
        <Card className="space-y-4">
            <h2 className="text-lg font-semibold">Spotify-Bibliothek</h2>
            <div className="flex flex-wrap gap-2">
                <Button
                    variant="ghost"
                    onClick={() => select({ query: "all_playlists" })}
                >
                    Playlists
                </Button>
                <Button
                    variant="ghost"
                    onClick={() => select({ query: "all_playlists" }, true)}
                >
                    Schnellzugriff
                </Button>
                {(
                    [
                        ["saved", "Favoriten"],
                        ["queue", "Warteschlange"],
                        ["recent", "Zuletzt gehört"],
                    ] as const
                ).map(([query, label]) => (
                    <Button
                        variant="ghost"
                        key={query}
                        onClick={() => select({ query })}
                    >
                        {label}
                    </Button>
                ))}
                <Button
                    variant="ghost"
                    onClick={() => {
                        void client.invalidateQueries({
                            queryKey: playlistsKey,
                        });
                        void refreshPlayback();
                    }}
                >
                    Bibliothek aktualisieren
                </Button>
            </div>
            <form
                className="flex gap-2"
                onSubmit={(event) => {
                    event.preventDefault();
                    if (search.trim())
                        select({ query: "search", text: search.trim() });
                }}
            >
                <Input
                    aria-label="Titel suchen"
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                />
                <Button type="submit" disabled={!search.trim()}>
                    Suchen
                </Button>
            </form>
            {error && <p role="alert">{String(error)}</p>}
            {message && <p role="status">{message}</p>}
            {preferences.data?.warnings?.map((warning) => (
                <p key={warning} role="alert">
                    {warning}
                </p>
            ))}
            {query.query === "all_playlists" ? (
                <>
                    <h3 className="font-medium">
                        {quick
                            ? "Favoriten und zuletzt gestartete Playlists"
                            : "Alle Playlists"}
                    </h3>
                    <label className="flex gap-2 items-center">
                        <input
                            type="checkbox"
                            checked={
                                settings.data?.Spotify
                                    .ShuffleSelectedPlaylist === true
                            }
                            disabled={!settings.data || busy}
                            onChange={(event) =>
                                preferences.mutate(event.target.checked)
                            }
                        />
                        Playlists mit Zufallswiedergabe starten
                    </label>
                    <label className="block">
                        Playlists filtern
                        <Input
                            value={filter}
                            onChange={(event) => setFilter(event.target.value)}
                        />
                    </label>
                    {playlists.isPending && <p>Playlists werden geladen…</p>}
                    {!playlists.isPending &&
                        !shown.length &&
                        !playlists.error && <p>Keine passenden Playlists.</p>}
                    <ul className="divide-y divide-border">
                        {shown.map((playlist) => {
                            const isFavorite = favorites.some(
                                (uri) =>
                                    uri.toLowerCase() ===
                                    playlist.uri?.toLowerCase(),
                            );
                            return (
                                <li
                                    key={playlist.id}
                                    className="flex flex-wrap items-center gap-2 py-2"
                                >
                                    {playlist.images?.[0]?.url && (
                                        <img
                                            className="h-12 w-12 rounded"
                                            src={playlist.images[0].url}
                                            alt=""
                                        />
                                    )}
                                    <div className="flex-1">
                                        <p>{playlist.name}</p>
                                        <p className="text-sm text-text-secondary">
                                            {playlist.owner?.display_name ??
                                                playlist.owner?.id}
                                            {typeof (
                                                playlist.items?.total ??
                                                playlist.tracks?.total
                                            ) === "number"
                                                ? ` · ${playlist.items?.total ?? playlist.tracks?.total} Titel`
                                                : ""}
                                        </p>
                                    </div>
                                    <Button
                                        disabled={busy || !playlist.uri}
                                        aria-label={`Abspielen: ${playlist.name}`}
                                        onClick={() =>
                                            perform({
                                                action: "play_playlist",
                                                uri: playlist.uri,
                                            })
                                        }
                                    >
                                        Abspielen
                                    </Button>
                                    <Button
                                        variant="ghost"
                                        aria-label={`Titel anzeigen: ${playlist.name}`}
                                        onClick={() => {
                                            setPlaylistName(playlist.name);
                                            select({
                                                query: "playlist_tracks",
                                                id: playlist.id,
                                            });
                                        }}
                                    >
                                        Titel anzeigen
                                    </Button>
                                    <Button
                                        variant="ghost"
                                        disabled={
                                            busy ||
                                            !settings.data ||
                                            !playlist.uri
                                        }
                                        aria-label={`${isFavorite ? "Playlist-Favorit entfernen" : "Playlist als Favorit merken"}: ${playlist.name}`}
                                        onClick={() =>
                                            favorite.mutate({
                                                uri: playlist.uri,
                                                favorite: !isFavorite,
                                            })
                                        }
                                    >
                                        {isFavorite
                                            ? "★ Favorit entfernen"
                                            : "☆ Favorit merken"}
                                    </Button>
                                </li>
                            );
                        })}
                    </ul>
                </>
            ) : (
                <>
                    <h3 className="font-medium">
                        {query.query === "playlist_tracks"
                            ? `Titel: ${playlistName}`
                            : query.query === "search"
                              ? "Suchergebnisse"
                              : query.query === "saved"
                                ? "Lieblingstitel"
                                : query.query === "queue"
                                  ? "Warteschlange"
                                  : "Zuletzt gehörte Titel"}
                    </h3>
                    {catalog.isPending && <p>Titel werden geladen…</p>}
                    {query.query === "queue" && page?.currently_playing && (
                        <p>Aktuell: {page.currently_playing.name}</p>
                    )}
                    {!catalog.isPending && !rows.length && !catalog.error && (
                        <p>Keine verfügbaren Titel auf dieser Seite.</p>
                    )}
                    <ul className="divide-y divide-border">
                        {rows.map(({ track, index, playedAt }) => {
                            const isSaved =
                                query.query === "saved" ||
                                (!!track.id && saved.data?.[track.id] === true);
                            const canPlay =
                                !!track.uri &&
                                track.is_playable !== false &&
                                !track.restrictions &&
                                !track.is_local;
                            return (
                                <li
                                    key={`${track.id ?? track.uri}-${index}`}
                                    className="flex flex-wrap items-center gap-2 py-2"
                                >
                                    {track.album?.images?.[0]?.url && (
                                        <img
                                            className="h-12 w-12 rounded"
                                            src={track.album.images[0].url}
                                            alt=""
                                        />
                                    )}
                                    <div className="flex-1">
                                        <p>{track.name}</p>
                                        <p className="text-sm text-text-secondary">
                                            {track.artists
                                                ?.map((artist) => artist.name)
                                                .join(", ")}
                                            {track.album?.name
                                                ? ` · ${track.album.name}`
                                                : ""}
                                        </p>
                                        {playedAt && (
                                            <p className="text-xs text-text-secondary">
                                                Gespielt:{" "}
                                                {new Date(
                                                    playedAt,
                                                ).toLocaleString("de-DE")}
                                            </p>
                                        )}
                                        {query.query === "queue" && (
                                            <p className="text-xs">
                                                Position {index + 1}
                                            </p>
                                        )}
                                        {!canPlay && (
                                            <p className="text-xs">
                                                Titel ist nicht verfügbar.
                                            </p>
                                        )}
                                    </div>
                                    <Button
                                        disabled={busy || !canPlay}
                                        onClick={() =>
                                            perform({
                                                action: "play_track",
                                                uri: track.uri,
                                            })
                                        }
                                    >
                                        Abspielen
                                    </Button>
                                    <Button
                                        variant="ghost"
                                        disabled={busy || !canPlay}
                                        onClick={() =>
                                            perform({
                                                action: "queue",
                                                uri: track.uri,
                                            })
                                        }
                                    >
                                        Einreihen
                                    </Button>
                                    <Button
                                        variant="ghost"
                                        disabled={
                                            busy ||
                                            !track.id ||
                                            (query.query !== "saved" &&
                                                !saved.data)
                                        }
                                        onClick={() =>
                                            track.id &&
                                            perform({
                                                action: isSaved
                                                    ? "remove_saved_track"
                                                    : "save_track",
                                                id: track.id,
                                            })
                                        }
                                    >
                                        {isSaved
                                            ? "Favorit entfernen"
                                            : "Als Favorit merken"}
                                    </Button>
                                </li>
                            );
                        })}
                    </ul>
                    {paged && (
                        <div className="flex gap-2 items-center">
                            <Button
                                variant="ghost"
                                disabled={offset === 0 || catalog.isFetching}
                                onClick={() =>
                                    setOffset(Math.max(0, offset - limit))
                                }
                            >
                                Vorherige Seite
                            </Button>
                            <span className="text-sm">
                                Seite {Math.floor(offset / limit) + 1}
                                {typeof page?.total === "number"
                                    ? ` · ${page.total} Einträge`
                                    : ""}
                            </span>
                            <Button
                                variant="ghost"
                                disabled={!more || catalog.isFetching}
                                onClick={() => setOffset(offset + limit)}
                            >
                                Nächste Seite
                            </Button>
                        </div>
                    )}
                </>
            )}
        </Card>
    );
}
