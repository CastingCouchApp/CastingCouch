import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    tauriInvoke,
    FALLBACK_POLL_MS,
    listenTwitchMetrics,
    listenTwitchGoals,
    type TwitchMetricsSnapshot,
    type TwitchCount,
} from "../../lib/api";
import type { GoalsDraft, GoalDraft } from "../../lib/command-contract";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Button } from "../../components/ui/button";
type GoalSnapshot = {
    original: unknown;
    draft: GoalsDraft;
    warnings: string[];
};
const goalKey = ["twitch-goals"] as const;
const metricsKey = ["twitch-metrics"] as const;
function Count({
    label,
    count,
    connected,
}: {
    label: string;
    count?: TwitchCount;
    connected?: boolean;
}) {
    return (
        <div aria-label={label}>
            <span className="text-muted">{label}: </span>
            <strong>{count?.value ?? "—"}</strong>
            {count?.value != null && (!connected || count.error) && (
                <span className="text-muted"> · veraltet</span>
            )}
            {count?.at && (
                <time className="block text-xs text-muted" dateTime={count.at}>
                    {new Date(count.at).toLocaleTimeString("de-DE")}
                </time>
            )}
        </div>
    );
}
export function TwitchGoals({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const [draft, setDraft] = useState<GoalsDraft>();
    const [original, setOriginal] = useState<unknown>();
    const [dirty, setDirty] = useState(false);
    const [listenerError, setListenerError] = useState<string>();
    const [attempt, setAttempt] = useState(0);
    const goals = useQuery({
        queryKey: goalKey,
        queryFn: () => tauriInvoke<GoalSnapshot>("twitch_goals_snapshot"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const metrics = useQuery({
        queryKey: metricsKey,
        queryFn: () =>
            tauriInvoke<TwitchMetricsSnapshot>("twitch_metrics_snapshot"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    useEffect(() => {
        if (goals.data && !dirty) {
            setDraft(structuredClone(goals.data.draft));
            setOriginal(goals.data.original);
        }
    }, [goals.data, dirty]);
    useEffect(() => {
        let disposed = false;
        const cleanup: Array<() => void> = [];
        setListenerError(undefined);
        const refresh = async (key: readonly string[]) => {
            await client.cancelQueries({ queryKey: key });
            if (!disposed) await client.invalidateQueries({ queryKey: key });
        };
        const register = (
            promise: Promise<() => void>,
            key: readonly string[],
        ) => {
            void promise
                .then((fn) => {
                    if (disposed) fn();
                    else {
                        cleanup.push(fn);
                        void refresh(key);
                    }
                })
                .catch((error) => {
                    if (!disposed) setListenerError(String(error));
                });
        };
        register(
            listenTwitchGoals(() => {
                if (!disposed) void refresh(goalKey);
            }),
            goalKey,
        );
        register(
            listenTwitchMetrics(() => {
                if (!disposed) void refresh(metricsKey);
            }),
            metricsKey,
        );
        void refresh(goalKey);
        void refresh(metricsKey);
        return () => {
            disposed = true;
            cleanup.forEach((fn) => fn());
        };
    }, [client, attempt]);
    const save = useMutation({
        mutationFn: () =>
            tauriInvoke<GoalSnapshot>("save_twitch_goals", {
                draft: draft!,
                original,
            }),
        onSuccess: (result) => {
            client.setQueryData(goalKey, result);
            setDraft(structuredClone(result.draft));
            setOriginal(result.original);
            setDirty(false);
            void client.invalidateQueries({ queryKey: ["settings"] });
        },
    });
    const refresh = useMutation({
        mutationFn: async () => {
            await client.cancelQueries({ queryKey: metricsKey });
            const result = await tauriInvoke<TwitchMetricsSnapshot>(
                "refresh_twitch_metrics",
            );
            client.setQueryData(metricsKey, result);
            if (listenerError) setAttempt((value) => value + 1);
        },
    });
    const reload = useMutation({
        mutationFn: async () => {
            if (
                dirty &&
                !window.confirm("Ungespeicherte Ziele verwerfen und neu laden?")
            )
                return;
            await client.cancelQueries({ queryKey: goalKey });
            const result = await tauriInvoke<GoalSnapshot>(
                "twitch_goals_snapshot",
            );
            client.setQueryData(goalKey, result);
            setDraft(structuredClone(result.draft));
            setOriginal(result.original);
            setDirty(false);
            save.reset();
            if (listenerError) setAttempt((value) => value + 1);
        },
    });
    const update = (
        key: "follower" | "subscriptions" | "donation",
        field: keyof GoalDraft,
        value: string,
    ) => {
        setDraft((previous) =>
            previous
                ? { ...previous, [key]: { ...previous[key], [field]: value } }
                : previous,
        );
        setDirty(true);
        save.reset();
    };
    const errors = [
        goals.error,
        metrics.error,
        listenerError,
        save.error,
        refresh.error,
        reload.error,
        metrics.data?.channelError,
        ...(
            ["viewerCount", "followers", "subscriptions", "chatters"] as const
        ).map((key) => metrics.data?.[key]?.error),
    ]
        .filter(Boolean)
        .map(String);
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">
                Kanalzahlen und Stream-Ziele
            </h2>
            {errors.length > 0 && (
                <p role="alert">{[...new Set(errors)].join(" · ")}</p>
            )}
            {save.isSuccess && <p role="status">Ziele gespeichert.</p>}
            {save.data?.warnings.map((warning, index) => (
                <p role="alert" key={index}>
                    {warning}
                </p>
            ))}
            <div className="grid grid-cols-2 gap-2">
                {[
                    ["viewerCount", "Zuschauer"],
                    ["followers", "Follower"],
                    ["subscriptions", "Abonnements"],
                    ["chatters", "Chatter"],
                ].map(([key, label]) => (
                    <Count
                        key={key}
                        label={label}
                        count={
                            metrics.data?.[
                                key as keyof Pick<
                                    TwitchMetricsSnapshot,
                                    | "viewerCount"
                                    | "followers"
                                    | "subscriptions"
                                    | "chatters"
                                >
                            ]
                        }
                        connected={metrics.data?.connected}
                    />
                ))}
            </div>
            <Button
                disabled={!enabled || refresh.isPending}
                onClick={() => refresh.mutate()}
            >
                Kanalzahlen aktualisieren
            </Button>
            {goals.isPending && <p role="status">Ziele werden geladen …</p>}
            {draft && (
                <fieldset
                    disabled={save.isPending || reload.isPending}
                    className="space-y-3"
                >
                    <label className="block">
                        OBS-Szene für Ziele
                        <Input
                            value={draft.overlayScene}
                            onChange={(e) => {
                                setDraft({
                                    ...draft,
                                    overlayScene: e.target.value,
                                });
                                setDirty(true);
                                save.reset();
                            }}
                        />
                    </label>
                    {(
                        [
                            ["follower", "Follower-Ziel"],
                            ["subscriptions", "Sub-Ziel"],
                            ["donation", "Donation-Ziel"],
                        ] as const
                    ).map(([key, label]) => (
                        <fieldset
                            key={key}
                            className="space-y-2 rounded border border-border p-3"
                        >
                            <legend>{label}</legend>
                            <label className="block">
                                {label} · Bezeichnung
                                <Input
                                    value={draft[key].title}
                                    onChange={(e) =>
                                        update(key, "title", e.target.value)
                                    }
                                />
                            </label>
                            <label className="block">
                                {label} · Ziel
                                <Input
                                    inputMode="decimal"
                                    value={draft[key].target}
                                    onChange={(e) =>
                                        update(key, "target", e.target.value)
                                    }
                                />
                            </label>
                            {key === "donation" ? (
                                <>
                                    <label className="block">
                                        {label} · Aktuell
                                        <Input
                                            inputMode="decimal"
                                            value={draft[key].current}
                                            onChange={(e) =>
                                                update(
                                                    key,
                                                    "current",
                                                    e.target.value,
                                                )
                                            }
                                        />
                                    </label>
                                    <label className="block">
                                        {label} · Währung
                                        <Input
                                            value={draft[key].currency}
                                            onChange={(e) =>
                                                update(
                                                    key,
                                                    "currency",
                                                    e.target.value,
                                                )
                                            }
                                        />
                                    </label>
                                    <label className="block">
                                        {label} · Grund
                                        <Input
                                            maxLength={160}
                                            value={draft[key].reason}
                                            onChange={(e) =>
                                                update(
                                                    key,
                                                    "reason",
                                                    e.target.value,
                                                )
                                            }
                                        />
                                    </label>
                                </>
                            ) : (
                                <Count
                                    label={`${label} · Aktuell`}
                                    count={
                                        metrics.data?.[
                                            key === "follower"
                                                ? "followers"
                                                : "subscriptions"
                                        ]
                                    }
                                    connected={metrics.data?.connected}
                                />
                            )}
                            <label className="block">
                                {label} · Schrift
                                <Input
                                    value={draft[key].fontFace}
                                    onChange={(e) =>
                                        update(key, "fontFace", e.target.value)
                                    }
                                />
                            </label>
                            <label className="block">
                                {label} · Schriftgröße
                                <Input
                                    inputMode="numeric"
                                    value={draft[key].fontSize}
                                    onChange={(e) =>
                                        update(key, "fontSize", e.target.value)
                                    }
                                />
                            </label>
                        </fieldset>
                    ))}
                </fieldset>
            )}
            <div className="flex flex-wrap gap-2">
                <Button
                    disabled={!draft || save.isPending || reload.isPending}
                    onClick={() => save.mutate()}
                >
                    Ziele speichern
                </Button>
                <Button
                    variant="ghost"
                    disabled={save.isPending || reload.isPending}
                    onClick={() => reload.mutate()}
                >
                    Ziele neu laden
                </Button>
            </div>
            <p className="text-sm text-muted">
                Follower und Subs werden von Twitch aktualisiert. Speichern
                aktualisiert vorhandene Goal-Bars in allen Canvas-Layouts.
            </p>
        </Card>
    );
}
