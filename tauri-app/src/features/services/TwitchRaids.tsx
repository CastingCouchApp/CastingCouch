import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    listenTwitchRaids,
    tauriInvoke,
    FALLBACK_POLL_MS,
} from "../../lib/api";
import type {
    RaidSuggestions,
    RaidTarget,
    RaidStarted,
    RaidState,
} from "../../lib/command-contract";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
type Settings = {
    channels: string[];
    selected: string;
    original: unknown;
    warnings?: string[];
};
export function raidLiveDuration(startedAt: string | null) {
    const start = startedAt ? Date.parse(startedAt) : NaN;
    if (!Number.isFinite(start)) return "";
    const minutes = Math.max(0, Math.floor((Date.now() - start) / 60000));
    return minutes >= 60
        ? `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")} Std.`
        : `${minutes} Min.`;
}
export function TwitchRaids({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const [list, setList] = useState<string>();
    const [listOriginal, setListOriginal] = useState<Settings>();
    const [selection, setSelection] = useState<string>();
    const [search, setSearch] = useState("");
    const [query, setQuery] = useState("");
    const [listenerError, setListenerError] = useState<string>();
    const [listenerAttempt, setListenerAttempt] = useState(0);
    const settings = useQuery({
        queryKey: ["twitch-raid-settings"],
        queryFn: () => tauriInvoke<Settings>("twitch_raid_settings"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const raid = useQuery({
        queryKey: ["twitch-raid-state"],
        queryFn: () => tauriInvoke<RaidState>("twitch_raid_state"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const login = settings.data?.selected ?? "";
    const target = useQuery({
        queryKey: ["twitch-raid-target", login, enabled],
        queryFn: () =>
            tauriInvoke<RaidTarget | null>("twitch_raid_target", { login }),
        enabled: enabled && !!login,
        refetchInterval: 30000,
        retry: false,
    });
    const suggestions = useQuery({
        queryKey: ["twitch-raid-suggestions", query, enabled],
        queryFn: () =>
            tauriInvoke<RaidSuggestions>("twitch_raid_suggestions", {
                query,
                force: false,
            }),
        enabled,
        refetchInterval: 60000,
        retry: false,
    });
    useEffect(() => {
        const timer = setTimeout(
            () => setQuery(search.trim().replace(/^@+/, "")),
            350,
        );
        return () => clearTimeout(timer);
    }, [search]);
    const refresh = async () => {
        await Promise.all([
            client.invalidateQueries({ queryKey: ["twitch-raid-settings"] }),
            client.invalidateQueries({ queryKey: ["twitch-raid-state"] }),
            client.invalidateQueries({ queryKey: ["twitch-raid-target"] }),
        ]);
    };
    useEffect(() => {
        let disposed = false;
        let stop: (() => void) | undefined;
        setListenerError(undefined);
        void listenTwitchRaids(() => {
            if (!disposed) {
                void client.invalidateQueries({
                    queryKey: ["twitch-raid-settings"],
                });
                void client.invalidateQueries({
                    queryKey: ["twitch-raid-state"],
                });
                void client.invalidateQueries({
                    queryKey: ["twitch-raid-target"],
                });
            }
        })
            .then((fn) => {
                if (disposed) fn();
                else {
                    stop = fn;
                    void client.invalidateQueries({
                        queryKey: ["twitch-raid-settings"],
                    });
                    void client.invalidateQueries({
                        queryKey: ["twitch-raid-state"],
                    });
                }
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            stop?.();
        };
    }, [client, listenerAttempt]);
    const select = useMutation({
        mutationFn: (login: string) =>
            tauriInvoke<Settings>("select_twitch_raid_target", { login }),
        onSuccess: async (data) => {
            client.setQueryData(["twitch-raid-settings"], data);
            setSelection(undefined);
            await refresh();
            void client.invalidateQueries({
                queryKey: ["twitch-raid-suggestions"],
            });
        },
    });
    const save = useMutation({
        mutationFn: () =>
            tauriInvoke<Settings>("save_twitch_raid_settings", {
                channels: (list ?? "").split(/[\r\n,;]+/),
                selected: listOriginal?.selected ?? login,
                original: listOriginal?.original ?? settings.data?.original,
            }),
        onSuccess: async (data) => {
            client.setQueryData(["twitch-raid-settings"], data);
            setList(undefined);
            setListOriginal(undefined);
            await refresh();
            void client.invalidateQueries({
                queryKey: ["twitch-raid-suggestions"],
            });
        },
    });
    const start = useMutation({
        mutationFn: () =>
            tauriInvoke<RaidStarted>("start_twitch_raid", { login }),
        onSettled: refresh,
    });
    const cancel = useMutation({
        mutationFn: () => tauriInvoke("cancel_twitch_raid"),
        onSettled: refresh,
    });
    const acknowledge = useMutation({
        mutationFn: () => tauriInvoke("acknowledge_twitch_raid"),
        onSettled: refresh,
    });
    const force = useMutation({
        mutationFn: (request: { query: string; enabled: boolean }) =>
            tauriInvoke<RaidSuggestions>("twitch_raid_suggestions", {
                query: request.query,
                force: true,
            }),
        onSuccess: (data, request) =>
            client.setQueryData(
                ["twitch-raid-suggestions", request.query, request.enabled],
                data,
            ),
    });
    const busy = start.isPending || cancel.isPending || acknowledge.isPending;
    const warnings = [
        ...(suggestions.data?.warnings ?? []),
        ...(start.data?.warnings ?? []),
        ...(save.data?.warnings ?? []),
        ...(select.data?.warnings ?? []),
    ];
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Community und Raids</h2>
            <label className="block">
                Ausgewähltes Raid-Ziel
                <Input
                    value={selection ?? login}
                    onChange={(e) => setSelection(e.target.value)}
                />
            </label>
            <Button
                disabled={
                    !settings.data ||
                    select.isPending ||
                    busy ||
                    !(selection ?? login).trim()
                }
                onClick={() => select.mutate(selection ?? login)}
            >
                Raid-Ziel speichern
            </Button>
            {login && (
                <a
                    className="underline"
                    href={`https://www.twitch.tv/${encodeURIComponent(login)}`}
                    target="_blank"
                    rel="noreferrer"
                >
                    Kanal öffnen: {login}
                </a>
            )}
            {!enabled && (
                <p>
                    Twitch nicht verbunden. Gespeicherte Ziele bleiben
                    bearbeitbar.
                </p>
            )}
            {target.data && enabled && (
                <div className="space-y-1">
                    {target.data.profileImageUrl && (
                        <img
                            className="w-12 h-12 rounded-full"
                            src={target.data.profileImageUrl}
                            alt=""
                        />
                    )}
                    <p>
                        {target.data.displayName} ·{" "}
                        {target.data.isOnline ? "Live" : "Offline"}
                    </p>
                    {target.data.isOnline && (
                        <>
                            <p>{target.data.title}</p>
                            <p>
                                {target.data.viewerCount} Zuschauer ·{" "}
                                {target.data.category}
                            </p>
                            <p>
                                Live seit{" "}
                                {raidLiveDuration(target.data.startedAt)}
                            </p>
                        </>
                    )}
                </div>
            )}
            {enabled &&
                login &&
                !target.isPending &&
                target.data === null &&
                !target.error && <p role="alert">Raid-Kanal nicht gefunden.</p>}
            <Button
                disabled={!enabled || !login || target.isFetching}
                onClick={() => void target.refetch()}
            >
                Ziel prüfen
            </Button>
            {raid.data?.requestedTarget && (
                <p role="status">
                    Start angefordert: {raid.data.requestedTarget}. Den
                    tatsächlichen Raid-Countdown in Twitch prüfen.
                </p>
            )}
            {raid.data?.lastError && <p role="alert">{raid.data.lastError}</p>}
            <div className="flex flex-wrap gap-2">
                <Button
                    disabled={
                        !enabled ||
                        busy ||
                        select.isPending ||
                        !!raid.error ||
                        raid.isPending ||
                        !!raid.data?.requestedTarget ||
                        !!target.error ||
                        target.isFetching ||
                        !target.data?.isOnline ||
                        !!selection
                    }
                    onClick={() => {
                        if (
                            window.confirm(
                                `Raid zu ${target.data?.displayName ?? login} starten?`,
                            )
                        )
                            start.mutate();
                    }}
                >
                    Raid starten
                </Button>
                <Button
                    disabled={!enabled || busy}
                    variant="danger"
                    onClick={() => {
                        if (
                            window.confirm(
                                "Den ausgehenden Twitch-Raid abbrechen?",
                            )
                        )
                            cancel.mutate();
                    }}
                >
                    Raid abbrechen
                </Button>
                {raid.data?.requestedTarget && (
                    <Button
                        disabled={busy}
                        onClick={() => {
                            if (
                                window.confirm(
                                    "Ist der Raid in Twitch bereits abgeschlossen? Diese Bestätigung setzt nur die lokale Anzeige zurück.",
                                )
                            )
                                acknowledge.mutate();
                        }}
                    >
                        Raid abgeschlossen bestätigen
                    </Button>
                )}
            </div>
            <label className="block">
                Raid-Kanäle suchen
                <Input
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                />
            </label>
            <Button
                disabled={!enabled || force.isPending}
                onClick={() => force.mutate({ query, enabled })}
            >
                Vorschläge aktualisieren
            </Button>
            <ul className="max-h-72 overflow-auto divide-y divide-border">
                {suggestions.data?.suggestions.map((item) => (
                    <li
                        key={item.login}
                        className="py-2 flex justify-between gap-2"
                    >
                        <span>
                            {item.displayName} ·{" "}
                            {item.isLive ? "Live" : "Offline"} ·{" "}
                            {item.sourceLabel}
                        </span>
                        <Button
                            aria-label={`${item.displayName} als Raid-Ziel wählen`}
                            disabled={select.isPending || busy}
                            onClick={() => select.mutate(item.login)}
                        >
                            Als Raid-Ziel wählen
                        </Button>
                    </li>
                ))}
            </ul>
            <label className="block">
                Gespeicherte Raid-Kanäle
                <textarea
                    disabled={!settings.data}
                    className="block w-full rounded-md border border-border bg-background p-2"
                    rows={4}
                    value={list ?? settings.data?.channels.join("\n") ?? ""}
                    onChange={(e) => {
                        if (list === undefined) setListOriginal(settings.data);
                        setList(e.target.value);
                    }}
                />
            </label>
            <p className="text-sm text-muted-foreground">
                Ein Kanal pro Zeile. Ein ausgewähltes Ziel wird vorne
                eingetragen; zuletzt verwendete Ziele sind auf 40 begrenzt.
            </p>
            <div className="flex gap-2">
                <Button
                    disabled={list === undefined || save.isPending}
                    onClick={() => save.mutate()}
                >
                    Raid-Liste speichern
                </Button>
                <Button
                    disabled={save.isPending}
                    onClick={() => {
                        setList(undefined);
                        setListOriginal(undefined);
                        setSelection(undefined);
                        void settings.refetch();
                    }}
                >
                    Gespeicherten Stand laden
                </Button>
            </div>
            {[
                settings.error,
                raid.error,
                target.error,
                suggestions.error,
                select.error,
                save.error,
                start.error,
                cancel.error,
                acknowledge.error,
                force.error,
                listenerError,
            ]
                .filter(Boolean)
                .map((error, index) => (
                    <p role="alert" key={`error-${index}`}>
                        {String(error)}
                    </p>
                ))}
            {warnings.map((warning, index) => (
                <p role="alert" key={`warning-${index}`}>
                    {warning}
                </p>
            ))}
            {listenerError && (
                <Button onClick={() => setListenerAttempt((x) => x + 1)}>
                    Live-Verbindung erneut versuchen
                </Button>
            )}
        </Card>
    );
}
