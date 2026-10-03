import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import {
    FALLBACK_POLL_MS,
    listenMusicStatisticsChanged,
    tauriInvoke,
} from "../../lib/api";

export type MusicStatisticsSnapshot = {
    totalPlays: number;
    totalListeningSeconds: number;
    topTracks: Array<{
        TrackId: string;
        Title: string;
        Artist: string;
        Album: string;
        PlayCount: number;
        ListeningSeconds: number;
        LastPlayedAt: string;
    }>;
    topArtists: Array<{
        artist: string;
        playCount: number;
        listeningSeconds: number;
    }>;
    error: string | null;
};
const key = ["music-statistics"] as const;
function duration(seconds: number) {
    const value = Math.max(0, Math.floor(seconds));
    return [Math.floor(value / 3600), Math.floor(value / 60) % 60, value % 60]
        .map((v) => String(v).padStart(2, "0"))
        .join(":");
}
function date(value: string) {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime())
        ? "Unbekannt"
        : parsed.toLocaleString("de-DE");
}
export function MusicStatistics() {
    const client = useQueryClient();
    const data = useQuery({
        queryKey: key,
        queryFn: () =>
            tauriInvoke<MusicStatisticsSnapshot>("music_statistics_snapshot"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const [confirm, setConfirm] = useState(false),
        [listenerError, setListenerError] = useState("");
    useEffect(() => {
        let disposed = false,
            unlisten: (() => void) | undefined;
        listenMusicStatisticsChanged(() => {
            void client.invalidateQueries({ queryKey: key });
        })
            .then((fn) => {
                if (disposed) fn();
                else unlisten = fn;
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [client]);
    const reset = useMutation({
        mutationFn: () => tauriInvoke<void>("reset_music_statistics"),
        onSuccess: async () => {
            setConfirm(false);
            await client.invalidateQueries({ queryKey: key });
        },
    });
    const snapshot = data.data;
    return (
        <Card className="space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 className="text-lg font-semibold">Spotify-Hörstatistik</h2>
                <div className="flex flex-wrap gap-2">
                    <Button
                        variant="ghost"
                        disabled={data.isFetching || reset.isPending}
                        onClick={() => void data.refetch()}
                    >
                        Aktualisieren
                    </Button>
                    <Button
                        variant="danger"
                        disabled={!snapshot || reset.isPending}
                        onClick={() => {
                            reset.reset();
                            setConfirm(true);
                        }}
                    >
                        Statistik zurücksetzen
                    </Button>
                </div>
            </div>
            <p className="text-sm text-text-secondary">
                Lokal gespeicherte Titelwechsel und Hörzeit des ausgewählten
                Spotify-Players. Erstmals erkannte pausierte Titel zählen
                ebenfalls.
            </p>
            {data.isPending && <p>Statistik wird geladen …</p>}
            {data.error && (
                <p role="alert" className="text-red-400">
                    {String(data.error)}
                </p>
            )}
            {snapshot?.error && (
                <p role="alert" className="text-red-400">
                    {snapshot.error}
                </p>
            )}
            {listenerError && (
                <p role="alert" className="text-red-400">
                    {listenerError}
                </p>
            )}
            {reset.error && (
                <p role="alert" className="text-red-400">
                    {String(reset.error)}
                </p>
            )}
            {confirm && (
                <div
                    role="group"
                    aria-label="Statistik zurücksetzen bestätigen"
                    className="rounded-lg border border-border p-3 space-y-3"
                >
                    <p>
                        Alle lokal gespeicherten Titelzähler und Hörzeiten
                        endgültig zurücksetzen?
                    </p>
                    <div className="flex gap-2">
                        <Button
                            variant="danger"
                            disabled={reset.isPending}
                            onClick={() => reset.mutate()}
                        >
                            Endgültig zurücksetzen
                        </Button>
                        <Button
                            variant="ghost"
                            disabled={reset.isPending}
                            onClick={() => setConfirm(false)}
                        >
                            Abbrechen
                        </Button>
                    </div>
                </div>
            )}
            {snapshot && (
                <>
                    <div className="flex flex-wrap gap-x-8 gap-y-2">
                        <p>{snapshot.totalPlays} erkannte Titel</p>
                        <p>
                            Hörzeit:{" "}
                            <strong>
                                {duration(snapshot.totalListeningSeconds)}
                            </strong>
                        </p>
                    </div>
                    {snapshot.topTracks.length === 0 ? (
                        <p>Noch keine Hörstatistik vorhanden.</p>
                    ) : (
                        <div className="grid gap-4 xl:grid-cols-2">
                            <section className="min-w-0 space-y-2">
                                <h3 className="font-semibold">
                                    Häufigste Titel
                                </h3>
                                <div className="overflow-x-auto">
                                    <table
                                        aria-label="Häufigste Titel"
                                        className="w-full text-left text-sm"
                                    >
                                        <thead>
                                            <tr className="border-b border-border">
                                                <th scope="col" className="p-2">
                                                    Titel
                                                </th>
                                                <th scope="col" className="p-2">
                                                    Anzahl
                                                </th>
                                                <th scope="col" className="p-2">
                                                    Hörzeit
                                                </th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {snapshot.topTracks.map((track) => (
                                                <tr
                                                    key={track.TrackId}
                                                    className="border-b border-border"
                                                >
                                                    <td className="p-2">
                                                        <p>
                                                            {track.Title ||
                                                                "Unbekannter Titel"}
                                                        </p>
                                                        <p className="text-text-secondary">
                                                            {track.Artist ||
                                                                "Unbekannter Interpret"}
                                                        </p>
                                                        {track.Album && (
                                                            <p className="text-text-secondary">
                                                                {track.Album}
                                                            </p>
                                                        )}
                                                        <p className="text-xs text-text-secondary">
                                                            Zuletzt erkannt:{" "}
                                                            {date(
                                                                track.LastPlayedAt,
                                                            )}
                                                        </p>
                                                    </td>
                                                    <td className="p-2">
                                                        {track.PlayCount}
                                                    </td>
                                                    <td className="p-2 whitespace-nowrap tabular-nums">
                                                        {duration(
                                                            track.ListeningSeconds,
                                                        )}
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            </section>
                            <section className="min-w-0 space-y-2">
                                <h3 className="font-semibold">
                                    Häufigste Interpreten
                                </h3>
                                <div className="overflow-x-auto">
                                    <table
                                        aria-label="Häufigste Interpreten"
                                        className="w-full text-left text-sm"
                                    >
                                        <thead>
                                            <tr className="border-b border-border">
                                                <th scope="col" className="p-2">
                                                    Interpret
                                                </th>
                                                <th scope="col" className="p-2">
                                                    Anzahl
                                                </th>
                                                <th scope="col" className="p-2">
                                                    Hörzeit
                                                </th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {snapshot.topArtists.map(
                                                (artist) => (
                                                    <tr
                                                        key={artist.artist}
                                                        className="border-b border-border"
                                                    >
                                                        <td className="p-2">
                                                            {artist.artist ||
                                                                "Unbekannter Interpret"}
                                                        </td>
                                                        <td className="p-2">
                                                            {artist.playCount}
                                                        </td>
                                                        <td className="p-2 whitespace-nowrap tabular-nums">
                                                            {duration(
                                                                artist.listeningSeconds,
                                                            )}
                                                        </td>
                                                    </tr>
                                                ),
                                            )}
                                        </tbody>
                                    </table>
                                </div>
                            </section>
                        </div>
                    )}
                </>
            )}
        </Card>
    );
}
