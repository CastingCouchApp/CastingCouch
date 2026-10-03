import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { save as saveDialog } from "@tauri-apps/plugin-dialog";
import { openPath } from "@tauri-apps/plugin-opener";
import {
    tauriInvoke,
    listenStreamHistory,
    FALLBACK_POLL_MS,
} from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
type Session = {
    SessionId?: string;
    StartedAt: string;
    EndedAt?: string | null;
    DurationSeconds: number;
    PeakViewers: number;
    AverageViewers: number;
    FollowersGained: number;
    ChatMessages?: number;
    AlertsPlayed?: number;
    NewSubscriptions?: number;
    GiftSubscriptions?: number;
    BitsCheered?: number;
    IncomingRaids?: number;
    Title?: string;
    Category?: string;
    Recovered?: boolean;
    ObservationAvailable?: boolean;
    Interrupted?: boolean;
    FollowersKnown?: boolean;
    ViewerSamples?: { Timestamp: string; ViewerCount: number }[];
};
type JournalEvent = {
    TimestampUtc: string;
    SessionId: string;
    Type: string;
    Payload: Record<string, unknown> | null;
};
type Snapshot = {
    active: Session | null;
    sessions: Session[];
    events: JournalEvent[];
    statistics: {
        totalStreams: number;
        totalDuration: string;
        averageViewers: number;
        peakViewers: number;
        followers: number;
        averageDuration: string;
        categories: {
            name: string;
            count: number;
            seconds: number;
            averageViewers: number;
        }[];
        development: Session[];
    };
    warnings: string[];
    directory: string;
};
const number = (value: number | undefined, digits = 0) =>
    (value ?? 0).toLocaleString("de-DE", {
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
    });
const date = (at: string) => {
    const value = new Date(at);
    return Number.isFinite(value.getTime())
        ? value.toLocaleString("de-DE")
        : "—";
};
function duration(seconds: number) {
    seconds = Math.max(0, Math.floor(seconds ?? 0));
    return [
        Math.floor(seconds / 3600),
        Math.floor(seconds / 60) % 60,
        seconds % 60,
    ]
        .map((n) => String(n).padStart(2, "0"))
        .join(":");
}
export function StreamHistory() {
    const client = useQueryClient();
    const [session, setSession] = useState<string>();
    const [listenerError, setListenerError] = useState<string>();
    const [attempt, setAttempt] = useState(0);
    const history = useQuery({
        queryKey: ["stream-history", session],
        queryFn: () =>
            tauriInvoke<Snapshot>("stream_history_snapshot", {
                sessionId: session ?? null,
            }),
        refetchInterval: FALLBACK_POLL_MS,
    });
    useEffect(() => {
        let disposed = false;
        let stop: (() => void) | undefined;
        let timer: ReturnType<typeof setTimeout> | undefined;
        setListenerError(undefined);
        const refresh = () => {
            if (disposed || timer) return;
            timer = setTimeout(() => {
                timer = undefined;
                void client.invalidateQueries({ queryKey: ["stream-history"] });
            }, 150);
        };
        void listenStreamHistory(refresh)
            .then((fn) => {
                if (disposed) fn();
                else {
                    stop = fn;
                    void client.invalidateQueries({
                        queryKey: ["stream-history"],
                    });
                }
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            stop?.();
            if (timer) clearTimeout(timer);
        };
    }, [client, attempt]);
    const retry = useMutation({
        mutationFn: () => tauriInvoke("retry_stream_history"),
        onSettled: () =>
            client.invalidateQueries({ queryKey: ["stream-history"] }),
    });
    const copy = useMutation({
        mutationFn: async () => {
            const text = await tauriInvoke<string>("latest_stream_summary");
            await navigator.clipboard.writeText(text);
        },
    });
    const exportHistory = useMutation({
        mutationFn: async (format: "csv" | "html") => {
            const path = await saveDialog({
                title:
                    format === "csv"
                        ? "Streamhistorie exportieren"
                        : "Stream-Report speichern",
                defaultPath: `twitch-history-${new Date().toISOString().slice(0, 10)}.${format}`,
                filters: [{ name: format.toUpperCase(), extensions: [format] }],
            });
            if (!path) return null;
            return tauriInvoke<string>("export_stream_history", {
                format,
                path,
            });
        },
    });
    const folder = useMutation({
        mutationFn: () => tauriInvoke("open_stream_history_folder"),
    });
    const open = useMutation({ mutationFn: (path: string) => openPath(path) });
    const stats = history.data?.statistics;
    return (
        <Card className="space-y-4">
            <h2 className="text-lg font-semibold">
                Sitzungen und Streamhistorie
            </h2>
            {history.isPending && (
                <p role="status">Sitzungsverlauf wird geladen …</p>
            )}
            {history.data?.active && (
                <div className="rounded border border-border p-3 space-y-2">
                    <h3 className="font-semibold">
                        {history.data.active.Recovered
                            ? "Sitzung nach Neustart: OBS-Status wird geprüft."
                            : "Laufende Sitzung"}
                    </h3>
                    {history.data.active.ObservationAvailable === false &&
                        !history.data.active.Recovered && (
                            <p role="status">
                                OBS-Status nicht verfügbar. Die Sitzung bleibt
                                geöffnet.
                            </p>
                        )}
                    <p>
                        {history.data.active.Title} ·{" "}
                        {duration(history.data.active.DurationSeconds)} · Peak{" "}
                        {number(history.data.active.PeakViewers)} · Ø{" "}
                        {number(history.data.active.AverageViewers, 1)}
                    </p>
                    <SessionCounters row={history.data.active} />
                </div>
            )}
            {stats && (
                <dl className="grid grid-cols-2 gap-3 md:grid-cols-3">
                    {[
                        ["Streams", stats.totalStreams, "Gesamtzahl Streams"],
                        ["Livezeit", stats.totalDuration, "Gesamte Livezeit"],
                        [
                            "Ø Zuschauer",
                            number(stats.averageViewers, 1),
                            "Durchschnittliche Zuschauer",
                        ],
                        ["Peak", stats.peakViewers, "Rekord-Peak"],
                        [
                            "Neue Follower",
                            stats.followers,
                            "Gesamter Followergewinn",
                        ],
                        [
                            "Ø Dauer",
                            stats.averageDuration,
                            "Durchschnittliche Streamdauer",
                        ],
                    ].map(([label, value, aria]) => (
                        <div key={String(label)}>
                            <dt className="text-sm text-muted-foreground">
                                {label}
                            </dt>
                            <dd
                                aria-label={String(aria)}
                                className="text-lg tabular-nums"
                            >
                                {value}
                            </dd>
                        </div>
                    ))}
                </dl>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    disabled={history.isFetching}
                    onClick={() => {
                        if (listenerError) setAttempt((x) => x + 1);
                        void history.refetch();
                    }}
                >
                    Verlauf aktualisieren
                </Button>
                <Button
                    disabled={retry.isPending}
                    onClick={() => retry.mutate()}
                >
                    Speicherung erneut versuchen
                </Button>
                <Button
                    disabled={copy.isPending || !history.data?.sessions.length}
                    onClick={() => copy.mutate()}
                >
                    Letzte Zusammenfassung kopieren
                </Button>
                <Button
                    disabled={exportHistory.isPending}
                    onClick={() => exportHistory.mutate("csv")}
                >
                    CSV exportieren
                </Button>
                <Button
                    disabled={
                        exportHistory.isPending ||
                        !history.data?.sessions.length
                    }
                    onClick={() => exportHistory.mutate("html")}
                >
                    HTML-Report speichern
                </Button>
                <Button
                    disabled={folder.isPending}
                    onClick={() => folder.mutate()}
                >
                    Verlaufsordner öffnen
                </Button>
                {exportHistory.data && (
                    <Button onClick={() => open.mutate(exportHistory.data!)}>
                        Export öffnen
                    </Button>
                )}
            </div>
            {copy.isSuccess && <p role="status">Zusammenfassung kopiert.</p>}
            {exportHistory.data && (
                <p role="status">Export gespeichert: {exportHistory.data}</p>
            )}
            {[
                history.error,
                retry.error,
                copy.error,
                exportHistory.error,
                folder.error,
                open.error,
                listenerError,
            ]
                .filter(Boolean)
                .map((error, index) => (
                    <p role="alert" key={`error-${index}`}>
                        {String(error)}
                    </p>
                ))}
            {history.data?.warnings.map((warning, index) => (
                <p role="alert" key={index}>
                    {warning}
                </p>
            ))}
            {history.data?.sessions.length === 0 && (
                <p>Noch keine abgeschlossenen Streams gespeichert.</p>
            )}
            <ol
                aria-label="Gespeicherte Streams"
                className="max-h-96 overflow-auto divide-y divide-border"
            >
                {history.data?.sessions.map((row, index) => (
                    <li
                        className="py-3 space-y-1"
                        key={row.SessionId ?? `${row.StartedAt}-${index}`}
                    >
                        <p className="font-semibold">
                            {row.Title || "Ohne Titel"}
                        </p>
                        <p>
                            {date(row.StartedAt)} ·{" "}
                            {row.Category || "Nicht angegeben"} ·{" "}
                            {duration(row.DurationSeconds)} · Peak{" "}
                            {number(row.PeakViewers)} · Ø{" "}
                            {number(row.AverageViewers, 1)}
                        </p>
                        <SessionCounters row={row} />
                        {row.Interrupted && (
                            <p>
                                Unterbrochene Sitzung; Laufzeit bis zum letzten
                                bestätigten Stand.
                            </p>
                        )}
                        <Button
                            disabled={!row.SessionId}
                            onClick={() => setSession(row.SessionId)}
                        >
                            Ereignisse dieser Sitzung
                        </Button>
                        {!!row.ViewerSamples?.length && (
                            <details>
                                <summary>
                                    Zuschauer-Samples (
                                    {row.ViewerSamples.length})
                                </summary>
                                <ol className="max-h-40 overflow-auto">
                                    {row.ViewerSamples.map((sample, i) => (
                                        <li key={i}>
                                            {date(sample.Timestamp)} ·{" "}
                                            {number(sample.ViewerCount)}{" "}
                                            Zuschauer
                                        </li>
                                    ))}
                                </ol>
                            </details>
                        )}
                    </li>
                ))}
            </ol>
            {!!stats?.categories.length && (
                <div>
                    <h3 className="font-semibold">Kategorien</h3>
                    <ul>
                        {stats.categories.map((category) => (
                            <li key={category.name}>
                                {category.name} · {category.count} Stream(s) ·{" "}
                                {number(category.seconds / 3600, 1)} h · Ø{" "}
                                {number(category.averageViewers, 1)} Viewer
                            </li>
                        ))}
                    </ul>
                </div>
            )}
            {!!stats?.development.length && (
                <div>
                    <h3 className="font-semibold">Entwicklung</h3>
                    <ol>
                        {stats.development.map((row, index) => (
                            <li key={index}>
                                {date(row.StartedAt)} · Ø{" "}
                                {number(row.AverageViewers, 1)} · Peak{" "}
                                {number(row.PeakViewers)} · +
                                {number(row.FollowersGained)} Follower
                            </li>
                        ))}
                    </ol>
                </div>
            )}
            <div>
                <h3 className="font-semibold">Dauerhaftes Ereignisjournal</h3>
                <p className="text-sm text-muted-foreground">
                    Bis zu 500 neueste Einträge. Die Dateien enthalten den
                    vollständigen gespeicherten Verlauf.
                </p>
                {session && (
                    <Button onClick={() => setSession(undefined)}>
                        Ereignisse aller Sitzungen
                    </Button>
                )}
                <ol
                    className="max-h-80 overflow-auto space-y-2"
                    aria-label="Gespeicherte Ereignisse"
                >
                    {history.data?.events.map((event, index) => (
                        <li key={index}>
                            <p>
                                <time dateTime={event.TimestampUtc}>
                                    {date(event.TimestampUtc)}
                                </time>{" "}
                                ·{" "}
                                <span>
                                    {String(
                                        event.Payload?.summary ??
                                            event.Payload?.Summary ??
                                            event.Type,
                                    )}
                                </span>
                            </p>
                            <details>
                                <summary>Details: {event.Type}</summary>
                                <pre className="whitespace-pre-wrap break-words text-xs">
                                    {JSON.stringify(event.Payload, null, 2)}
                                </pre>
                            </details>
                        </li>
                    ))}
                </ol>
            </div>
        </Card>
    );
}
function SessionCounters({ row }: { row: Session }) {
    return (
        <p className="text-sm">
            Follower:{" "}
            {row.FollowersKnown === false
                ? "nicht verfügbar"
                : number(row.FollowersGained)}{" "}
            · Chat: {number(row.ChatMessages)} · Alerts:{" "}
            {number(row.AlertsPlayed)} · Subs: {number(row.NewSubscriptions)} ·
            Geschenke: {number(row.GiftSubscriptions)} · Bits:{" "}
            {number(row.BitsCheered)} · Raids: {number(row.IncomingRaids)}
        </p>
    );
}
