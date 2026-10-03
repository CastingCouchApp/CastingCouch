import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { NotificationSnapshot } from "../../lib/command-contract";
import {
    FALLBACK_POLL_MS,
    listenNotificationsChanged,
    tauriInvoke,
} from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";

export function Notifications() {
    const client = useQueryClient();
    const [filter, setFilter] = useState("Alle");
    const [listenerError, setListenerError] = useState<string>();
    const [attempt, setAttempt] = useState(0);
    const journal = useQuery({
        queryKey: ["notifications", filter],
        queryFn: () =>
            tauriInvoke<NotificationSnapshot>("notifications_snapshot", {
                filter,
            }),
        refetchInterval: FALLBACK_POLL_MS,
    });
    useEffect(() => {
        let disposed = false;
        let stop: (() => void) | undefined;
        setListenerError(undefined);
        void listenNotificationsChanged(() => {
            if (!disposed)
                void client.invalidateQueries({ queryKey: ["notifications"] });
        })
            .then((fn) => {
                if (disposed) fn();
                else stop = fn;
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            stop?.();
        };
    }, [client, attempt]);
    const edit = useMutation({
        mutationFn: (action: "read" | "clear" | "retry") => {
            if (action === "read")
                return tauriInvoke("notifications_mark_read");
            if (action === "clear") return tauriInvoke("notifications_clear");
            return tauriInvoke("notifications_retry");
        },
        onSettled: () =>
            client.invalidateQueries({ queryKey: ["notifications"] }),
    });
    const data = journal.data;
    return (
        <Card className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="text-lg font-semibold">Benachrichtigungen</h2>
                {data && (
                    <span
                        className="text-sm text-text-secondary"
                        aria-live="polite"
                    >
                        {data.unreadCount
                            ? `${data.unreadCount} ungelesen`
                            : `${data.total} Meldungen`}
                    </span>
                )}
            </div>
            <label className="flex flex-wrap items-center gap-2 text-sm">
                Benachrichtigungen filtern
                <select
                    className="rounded-md border border-border bg-input p-2"
                    value={filter}
                    onChange={(event) => setFilter(event.target.value)}
                >
                    {["Alle", "Info", "Warnungen", "Fehler"].map((value) => (
                        <option key={value}>{value}</option>
                    ))}
                </select>
            </label>
            <div className="flex flex-wrap gap-2">
                <Button
                    variant="ghost"
                    disabled={!data || edit.isPending || data.unreadCount === 0}
                    onClick={() => edit.mutate("read")}
                >
                    Alle als gelesen markieren
                </Button>
                <Button
                    variant="ghost"
                    disabled={!data || edit.isPending || data.total === 0}
                    onClick={() => {
                        if (
                            window.confirm(
                                "Alle gespeicherten Benachrichtigungen löschen?",
                            )
                        )
                            edit.mutate("clear");
                    }}
                >
                    Benachrichtigungen leeren
                </Button>
            </div>
            {journal.isPending && (
                <p role="status">Meldungen werden geladen …</p>
            )}
            {journal.error && (
                <p role="alert">
                    Benachrichtigungen konnten nicht aktualisiert werden:{" "}
                    {String(journal.error)}
                </p>
            )}
            {edit.error && <p role="alert">{String(edit.error)}</p>}
            {listenerError && (
                <div role="alert">
                    <p>Live-Aktualisierung nicht verfügbar: {listenerError}</p>
                    <Button
                        variant="ghost"
                        onClick={() => setAttempt((n) => n + 1)}
                    >
                        Live-Aktualisierung erneut verbinden
                    </Button>
                </div>
            )}
            {!!data?.warnings.length && (
                <div role="alert" className="space-y-2">
                    {data.warnings.map((warning) => (
                        <p key={warning}>{warning}</p>
                    ))}
                    <Button
                        variant="ghost"
                        disabled={edit.isPending}
                        onClick={() => edit.mutate("retry")}
                    >
                        Speichern erneut versuchen
                    </Button>
                </div>
            )}
            {data?.recoveryBackup && (
                <p className="break-all text-sm text-text-secondary">
                    Originaldatei gesichert: {data.recoveryBackup}
                </p>
            )}
            {data?.entries.length === 0 && (
                <p className="text-sm text-text-secondary">
                    {filter === "Alle"
                        ? "Keine Benachrichtigungen."
                        : "Keine Meldungen für diesen Filter."}
                </p>
            )}
            <ul
                className="max-h-80 space-y-2 overflow-auto"
                aria-label="App-Benachrichtigungen"
            >
                {data?.entries.map((entry, index) => (
                    <li
                        key={`${entry.timestamp}-${index}`}
                        className="rounded-md bg-input p-2 text-sm"
                    >
                        <div className="flex flex-wrap items-center gap-2 text-text-secondary">
                            <span
                                className={
                                    entry.severity === "Fehler"
                                        ? "text-danger"
                                        : ""
                                }
                            >
                                {entry.severity === "Fehler"
                                    ? "✕"
                                    : entry.severity === "Warnung"
                                      ? "⚠"
                                      : "ℹ"}{" "}
                                {entry.severity}
                            </span>
                            <time dateTime={entry.timestamp}>
                                {new Date(entry.timestamp).toLocaleString(
                                    "de-DE",
                                )}
                            </time>
                            {!entry.isRead && (
                                <span aria-label="Ungelesen">•</span>
                            )}
                        </div>
                        <p className="whitespace-pre-wrap break-words">
                            {entry.message}
                        </p>
                    </li>
                ))}
            </ul>
        </Card>
    );
}
