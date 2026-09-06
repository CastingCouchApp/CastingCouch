import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { cloneSettings, type AppSettings } from "../lib/app-settings";
import { Card } from "../components/ui/card";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";
import {
    FALLBACK_POLL_MS,
    queryKeys,
    tauriInvoke,
    type NowPlaying,
    type YtmNowPlaying,
} from "../lib/api";
export const Route = createFileRoute("/music")({ component: MusicPage });
type Track = {
    id: string;
    uri: string;
    name: string;
    artists?: { name: string }[];
    track?: Track;
};
type Catalog = {
    items?: Track[];
    tracks?: { items: Track[] };
    queue?: Track[];
};
function MusicPage() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const provider = useMutation({
        mutationFn: async (source: string) => {
            if (!settings.data) throw new Error("Einstellungen fehlen");
            const next = cloneSettings(settings.data);
            next.MusicPlayer = { ...next.MusicPlayer, Source: source };
            await tauriInvoke("save_settings", {
                original: settings.data,
                settings: next,
            });
        },
        onSuccess: () =>
            client.invalidateQueries({ queryKey: queryKeys.settings }),
    });
    const playback = useQuery({
        queryKey: ["spotify-playback"],
        queryFn: () =>
            tauriInvoke<{
                shuffle_state: boolean;
                repeat_state: string;
                device?: { volume_percent: number };
            }>("spotify_query", { query: { query: "playback" } }),
        refetchInterval: 5000,
    });
    const [setup, setSetup] = useState("");
    const [search, setSearch] = useState("");
    const [catalogQuery, setCatalogQuery] = useState<Record<string, string>>({
        query: "playlists",
    });
    const [offset, setOffset] = useState(0);
    const now = useQuery({
        queryKey: queryKeys.nowPlaying,
        queryFn: () => tauriInvoke<NowPlaying>("now_playing"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const ytm = useQuery({
        queryKey: queryKeys.ytmNowPlaying,
        queryFn: () => tauriInvoke<YtmNowPlaying>("ytm_now_playing"),
        refetchInterval: 2000,
    });
    const devices = useQuery({
        queryKey: ["spotify-devices"],
        queryFn: () =>
            tauriInvoke<{
                devices: { id: string; name: string; is_active: boolean }[];
            }>("spotify_query", { query: { query: "devices" } }),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const catalog = useQuery({
        queryKey: ["spotify-catalog", catalogQuery, offset],
        queryFn: () =>
            tauriInvoke<Catalog>("spotify_query", {
                query: catalogQuery,
                offset,
            }),
    });
    const act = useMutation({
        mutationFn: (action: Record<string, unknown>) =>
            tauriInvoke("spotify_action", { action }),
        onSuccess: () => {
            void client.invalidateQueries({ queryKey: ["spotify-playback"] });
            void client.invalidateQueries({ queryKey: queryKeys.nowPlaying });
            void client.invalidateQueries({ queryKey: ["spotify-catalog"] });
            void client.invalidateQueries({ queryKey: ["spotify-devices"] });
        },
    });
    const ytmAct = useMutation({
        mutationFn: (command: string) =>
            tauriInvoke("ytm_command", { command }),
    });
    const connect = useMutation({
        mutationFn: () => tauriInvoke<string>("ytm_connect"),
        onSuccess: setSetup,
    });
    const disconnect = useMutation({
        mutationFn: () => tauriInvoke("ytm_disconnect"),
        onSuccess: () => {
            setSetup("");
            void client.invalidateQueries({
                queryKey: queryKeys.ytmNowPlaying,
            });
        },
    });
    const items =
        catalog.data?.items ??
        catalog.data?.tracks?.items ??
        catalog.data?.queue ??
        [];
    const error =
        provider.error ??
        act.error ??
        ytmAct.error ??
        connect.error ??
        disconnect.error;
    return (
        <div className="space-y-6">
            <h1 className="text-2xl font-semibold">Musik</h1>
            {error && (
                <p role="alert" className="text-red-400">
                    {String(error)}
                </p>
            )}
            <label className="block">
                Musikprovider für das Overlay{" "}
                <select
                    aria-label="Musikprovider für das Overlay"
                    className="bg-panel p-2"
                    disabled={!settings.data || provider.isPending}
                    value={String(
                        settings.data?.MusicPlayer?.Source || "spotify",
                    ).toLowerCase()}
                    onChange={(e) => provider.mutate(e.target.value)}
                >
                    <option value="spotify">Spotify</option>
                    <option value="ytmusic">YouTube Music</option>
                </select>
            </label>
            <div className="grid gap-4 xl:grid-cols-2">
                <Card className="space-y-4">
                    <h2 className="text-lg font-semibold">Spotify</h2>
                    <p>{now.data?.title || "Keine Wiedergabe"}</p>
                    <p className="text-text-secondary">{now.data?.artist}</p>
                    <div className="flex flex-wrap gap-2">
                        <Button
                            disabled={act.isPending}
                            onClick={() => act.mutate({ action: "previous" })}
                        >
                            Vorheriger Titel
                        </Button>
                        <Button
                            disabled={act.isPending}
                            onClick={() =>
                                act.mutate({
                                    action: now.data?.is_playing
                                        ? "pause"
                                        : "play",
                                })
                            }
                        >
                            {now.data?.is_playing
                                ? "Spotify pausieren"
                                : "Spotify abspielen"}
                        </Button>
                        <Button
                            disabled={act.isPending}
                            onClick={() => act.mutate({ action: "next" })}
                        >
                            Nächster Titel
                        </Button>
                    </div>
                    <label className="block">
                        Lautstärke{" "}
                        <input
                            aria-label="Spotify Lautstärke"
                            type="range"
                            min="0"
                            max="100"
                            key={playback.data?.device?.volume_percent}
                            defaultValue={
                                playback.data?.device?.volume_percent ?? 0
                            }
                            disabled={!playback.data?.device}
                            onPointerUp={(e) =>
                                act.mutate({
                                    action: "volume",
                                    percent: Number(e.currentTarget.value),
                                })
                            }
                            onKeyUp={(e) => {
                                if (e.key.startsWith("Arrow"))
                                    act.mutate({
                                        action: "volume",
                                        percent: Number(e.currentTarget.value),
                                    });
                            }}
                        />
                    </label>
                    <label className="block">
                        Position (Sekunden, Enter bestätigt)
                        <Input
                            type="number"
                            min="0"
                            defaultValue="0"
                            onKeyDown={(e) => {
                                if (e.key === "Enter")
                                    act.mutate({
                                        action: "seek",
                                        positionMs: Math.max(
                                            0,
                                            Number(e.currentTarget.value) *
                                                1000,
                                        ),
                                    });
                            }}
                        />
                    </label>
                    <label className="flex gap-2">
                        <input
                            type="checkbox"
                            checked={playback.data?.shuffle_state ?? false}
                            onChange={(e) =>
                                act.mutate({
                                    action: "shuffle",
                                    enabled: e.target.checked,
                                })
                            }
                        />
                        Zufallswiedergabe
                    </label>
                    <label className="block">
                        Wiederholung{" "}
                        <select
                            className="bg-panel p-2"
                            value={playback.data?.repeat_state ?? "off"}
                            onChange={(e) =>
                                act.mutate({
                                    action: "repeat",
                                    mode: e.target.value,
                                })
                            }
                        >
                            <option value="off">Aus</option>
                            <option value="context">Playlist</option>
                            <option value="track">Titel</option>
                        </select>
                    </label>
                    <label className="block">
                        Wiedergabegerät{" "}
                        <select
                            aria-label="Wiedergabegerät"
                            className="bg-panel p-2"
                            value={
                                devices.data?.devices?.find((d) => d.is_active)
                                    ?.id ?? ""
                            }
                            onChange={(e) =>
                                act.mutate({
                                    action: "transfer",
                                    deviceId: e.target.value,
                                })
                            }
                        >
                            <option value="">Gerät auswählen</option>
                            {devices.data?.devices?.map((d) => (
                                <option key={d.id} value={d.id}>
                                    {d.name}
                                </option>
                            ))}
                        </select>
                    </label>
                    {devices.error && (
                        <p className="text-text-secondary">
                            {String(devices.error)}
                        </p>
                    )}
                </Card>
                <Card className="space-y-4">
                    <h2 className="text-lg font-semibold">YouTube Music</h2>
                    <p>
                        {ytm.data?.title ||
                            ytm.data?.statusText ||
                            "Nicht verbunden"}
                    </p>
                    <p>{ytm.data?.artist}</p>
                    <div className="flex flex-wrap gap-2">
                        <Button
                            disabled={connect.isPending}
                            onClick={() => connect.mutate()}
                        >
                            YouTube Music verbinden
                        </Button>
                        <Button onClick={() => disconnect.mutate()}>
                            Trennen
                        </Button>
                    </div>
                    {setup && (
                        <a
                            className="underline"
                            href={setup}
                            target="_blank"
                            rel="noreferrer"
                        >
                            Bookmarklet einrichten
                        </a>
                    )}
                    <div className="flex gap-2">
                        {[
                            ["previous", "Zurück"],
                            ["playpause", "Play/Pause"],
                            ["next", "Weiter"],
                        ].map(([command, label]) => (
                            <Button
                                key={command}
                                disabled={
                                    !ytm.data?.connected || ytmAct.isPending
                                }
                                onClick={() => ytmAct.mutate(command)}
                            >
                                {label}
                            </Button>
                        ))}
                    </div>
                </Card>
            </div>
            <Card className="space-y-4">
                <h2 className="text-lg font-semibold">Spotify-Bibliothek</h2>
                <div className="flex flex-wrap gap-2">
                    {[
                        ["playlists", "Playlists"],
                        ["saved", "Favoriten"],
                        ["queue", "Warteschlange"],
                        ["recent", "Zuletzt gehört"],
                    ].map(([query, label]) => (
                        <Button
                            key={query}
                            onClick={() => {
                                setOffset(0);
                                setCatalogQuery({ query });
                            }}
                        >
                            {label}
                        </Button>
                    ))}
                </div>
                <form
                    className="flex gap-2"
                    onSubmit={(e) => {
                        e.preventDefault();
                        setOffset(0);
                        setCatalogQuery({ query: "search", text: search });
                    }}
                >
                    <Input
                        aria-label="Titel suchen"
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                    />
                    <Button type="submit">Suchen</Button>
                </form>
                {catalog.error && <p role="alert">{String(catalog.error)}</p>}
                {catalog.isLoading && <p>Lädt…</p>}
                <ul className="divide-y divide-border">
                    {items.map((item, index) => {
                        const track = item.track ?? item;
                        return (
                            <li
                                key={`${track.id}-${index}`}
                                className="flex flex-wrap items-center gap-2 py-2"
                            >
                                <span className="flex-1">
                                    {track.name}
                                    {track.artists?.length
                                        ? ` · ${track.artists.map((a) => a.name).join(", ")}`
                                        : ""}
                                </span>
                                {catalogQuery.query === "playlists" ? (
                                    <>
                                        <Button
                                            onClick={() =>
                                                act.mutate({
                                                    action: "play_playlist",
                                                    uri: track.uri,
                                                })
                                            }
                                        >
                                            Abspielen
                                        </Button>
                                        <Button
                                            onClick={() => {
                                                setOffset(0);
                                                setCatalogQuery({
                                                    query: "playlist_tracks",
                                                    id: track.id,
                                                });
                                            }}
                                        >
                                            Titel anzeigen
                                        </Button>
                                    </>
                                ) : (
                                    <>
                                        <Button
                                            onClick={() =>
                                                act.mutate({
                                                    action: "play_track",
                                                    uri: track.uri,
                                                })
                                            }
                                        >
                                            Abspielen
                                        </Button>
                                        <Button
                                            onClick={() =>
                                                act.mutate({
                                                    action: "queue",
                                                    uri: track.uri,
                                                })
                                            }
                                        >
                                            Einreihen
                                        </Button>
                                        <Button
                                            onClick={() =>
                                                act.mutate({
                                                    action:
                                                        catalogQuery.query ===
                                                        "saved"
                                                            ? "remove_saved_track"
                                                            : "save_track",
                                                    id: track.id,
                                                })
                                            }
                                        >
                                            {catalogQuery.query === "saved"
                                                ? "Entfernen"
                                                : "Merken"}
                                        </Button>
                                    </>
                                )}
                            </li>
                        );
                    })}
                </ul>
                <div className="flex gap-2">
                    <Button
                        disabled={!offset}
                        onClick={() => setOffset(Math.max(0, offset - 50))}
                    >
                        Vorherige Seite
                    </Button>
                    <Button
                        disabled={
                            items.length < 50 ||
                            ["queue", "recent"].includes(catalogQuery.query)
                        }
                        onClick={() => setOffset(offset + 50)}
                    >
                        Nächste Seite
                    </Button>
                </div>
            </Card>
        </div>
    );
}
