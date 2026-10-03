import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import {
    listenMusicPlayer,
    queryKeys,
    tauriInvoke,
    type MusicPlayerSnapshot,
} from "../../lib/api";
import type { MusicPlayerAction } from "../../lib/command-contract";

function time(ms: number) {
    const seconds = Math.floor(Math.max(0, ms) / 1000),
        minutes = Math.floor(seconds / 60);
    return minutes >= 60
        ? `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`
        : `${minutes}:${String(seconds % 60).padStart(2, "0")}`;
}
const rangeKeys = new Set([
    "ArrowLeft",
    "ArrowRight",
    "ArrowUp",
    "ArrowDown",
    "Home",
    "End",
    "PageUp",
    "PageDown",
]);
export function CommonMusicPlayer({
    provider,
    changingProvider = false,
}: {
    provider: string;
    changingProvider?: boolean;
}) {
    const client = useQueryClient();
    const now = useQuery({
        queryKey: queryKeys.musicPlayer,
        queryFn: () =>
            tauriInvoke<MusicPlayerSnapshot>("music_player_snapshot"),
        refetchInterval: 2000,
    });
    const [position, setPosition] = useState<number | null>(null),
        [volume, setVolume] = useState<number | null>(null);
    useEffect(() => {
        let disposed = false;
        let unlisten: (() => void) | undefined;
        void listenMusicPlayer((snapshot) =>
            client.setQueryData(queryKeys.musicPlayer, snapshot),
        ).then((fn) => {
            if (disposed) fn();
            else unlisten = fn;
        });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [client]);
    useEffect(() => {
        setPosition(null);
        setVolume(null);
    }, [provider, now.data?.title, now.data?.artist]);
    const operation = useMutation({
        mutationFn: (run: () => Promise<unknown>) => run(),
        onSuccess: async () => {
            setPosition(null);
            setVolume(null);
            await Promise.all(
                [
                    queryKeys.musicPlayer,
                    queryKeys.nowPlaying,
                    ["spotify-playback"],
                    ["spotify-devices"],
                    ["ytm-runtime"],
                ].map((queryKey) => client.invalidateQueries({ queryKey })),
            );
        },
    });
    const snapshot =
        !changingProvider && now.data?.provider === provider
            ? now.data
            : undefined;
    const busy = operation.isPending || changingProvider || !snapshot;
    const act = (action: MusicPlayerAction) =>
        operation.mutate(() => tauriInvoke("music_player_action", { action }));
    const duration = snapshot?.durationMs ?? 0;
    const currentPosition = Math.min(
        duration,
        position ?? snapshot?.progressMs ?? 0,
    );
    const error = operation.error ?? now.error ?? snapshot?.error;
    return (
        <Card className="space-y-4">
            <h2 className="text-lg font-semibold">
                Musikplayer ·{" "}
                {snapshot?.providerDisplayName ||
                    (provider === "ytmusic" ? "YouTube Music" : "Spotify")}
            </h2>
            <div className="flex items-center gap-4">
                {snapshot?.coverUrl && (
                    <img
                        src={snapshot.coverUrl}
                        alt="Albumcover"
                        className="h-36 w-36 rounded object-cover"
                    />
                )}
                <div>
                    <p className="font-semibold">
                        {snapshot?.title || "Keine Wiedergabe"}
                    </p>
                    <p>{snapshot?.artist}</p>
                    <p className="text-text-secondary">{snapshot?.album}</p>
                </div>
            </div>
            <p role="status">
                {changingProvider
                    ? "Musikprovider wird gewechselt …"
                    : snapshot?.statusText || "Musikstatus wird geladen …"}
            </p>
            {error && (
                <p role="alert" className="text-red-400">
                    {String(error)}
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    disabled={busy || !snapshot?.connected}
                    onClick={() => act({ action: "previous" })}
                >
                    Vorheriger Musiktitel
                </Button>
                <Button
                    disabled={busy || !snapshot?.connected}
                    onClick={() => act({ action: "play_pause" })}
                >
                    {snapshot?.isPlaying
                        ? "Musik pausieren"
                        : "Musik abspielen"}
                </Button>
                <Button
                    disabled={busy || !snapshot?.connected}
                    onClick={() => act({ action: "next" })}
                >
                    Nächster Musiktitel
                </Button>
                <Button
                    disabled={
                        busy ||
                        snapshot?.connected ||
                        snapshot?.connecting ||
                        snapshot?.bridgeRunning
                    }
                    onClick={() =>
                        operation.mutate(() =>
                            tauriInvoke("music_player_connect"),
                        )
                    }
                >
                    Musik verbinden
                </Button>
                <Button
                    disabled={
                        busy ||
                        (!snapshot?.connected &&
                            !snapshot?.connecting &&
                            !snapshot?.bridgeRunning &&
                            !snapshot?.error)
                    }
                    onClick={() =>
                        operation.mutate(() =>
                            tauriInvoke("music_player_disconnect"),
                        )
                    }
                >
                    Musik trennen
                </Button>
            </div>
            <p>
                {time(currentPosition)} / {time(duration)}
            </p>
            {snapshot?.supportsSeek && (
                <label className="block">
                    Wiedergabeposition
                    <input
                        aria-label="Wiedergabeposition"
                        className="w-full accent-[var(--color-accent)]"
                        type="range"
                        min="0"
                        max={duration}
                        step="1000"
                        value={currentPosition}
                        disabled={busy || !snapshot.connected || duration <= 0}
                        onChange={(e) =>
                            setPosition(Number(e.currentTarget.value))
                        }
                        onPointerUp={(e) =>
                            act({
                                action: "seek",
                                positionMs: Number(e.currentTarget.value),
                            })
                        }
                        onKeyUp={(e) => {
                            if (rangeKeys.has(e.key))
                                act({
                                    action: "seek",
                                    positionMs: Number(e.currentTarget.value),
                                });
                        }}
                    />
                </label>
            )}
            {snapshot?.supportsVolume && (
                <label className="block">
                    {snapshot.volumePercent === null
                        ? "Lautstärke unbekannt"
                        : `Lautstärke: ${volume ?? snapshot.volumePercent} %`}
                    <input
                        aria-label="Musiklautstärke"
                        className="w-full accent-[var(--color-accent)]"
                        type="range"
                        min="0"
                        max="100"
                        value={volume ?? snapshot.volumePercent ?? 0}
                        disabled={
                            busy ||
                            !snapshot.connected ||
                            snapshot.volumePercent === null
                        }
                        onChange={(e) =>
                            setVolume(Number(e.currentTarget.value))
                        }
                        onPointerUp={(e) =>
                            act({
                                action: "volume",
                                percent: Number(e.currentTarget.value),
                            })
                        }
                        onKeyUp={(e) => {
                            if (rangeKeys.has(e.key))
                                act({
                                    action: "volume",
                                    percent: Number(e.currentTarget.value),
                                });
                        }}
                    />
                </label>
            )}
        </Card>
    );
}
