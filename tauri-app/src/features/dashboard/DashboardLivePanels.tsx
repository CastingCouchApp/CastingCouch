import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import {
    tauriInvoke,
    FALLBACK_POLL_MS,
    listenStreamHistory,
    listenTwitchMetrics,
    type TwitchCount,
    type TwitchMetricsSnapshot,
} from "../../lib/api";
const metricsKey = ["twitch-metrics"] as const;
const historyKey = ["stream-history", undefined] as const;
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
export function DashboardCommunity({ statistic }: { statistic: string }) {
    const client = useQueryClient();
    const [listenerError, setListenerError] = useState<string>();
    const [attempt, setAttempt] = useState(0);
    const metrics = useQuery({
        queryKey: metricsKey,
        queryFn: () =>
            tauriInvoke<TwitchMetricsSnapshot>("twitch_metrics_snapshot"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const history = useQuery({
        queryKey: historyKey,
        queryFn: () =>
            tauriInvoke<{
                active: {
                    FollowersKnown?: boolean;
                    FollowersGained?: number;
                    NewSubscriptions?: number;
                    ObservationAvailable?: boolean;
                    ViewerSamples?: { ViewerCount: number }[];
                } | null;
            }>("stream_history_snapshot", { sessionId: null }),
        refetchInterval: FALLBACK_POLL_MS,
    });
    useEffect(() => {
        let disposed = false;
        const stops: Array<() => void> = [];
        setListenerError(undefined);
        const refresh = async (key: readonly unknown[]) => {
            await client.cancelQueries({ queryKey: key });
            if (!disposed) await client.invalidateQueries({ queryKey: key });
        };
        const register = (
            promise: Promise<() => void>,
            key: readonly unknown[],
        ) => {
            void promise
                .then((fn) => {
                    if (disposed) fn();
                    else {
                        stops.push(fn);
                        void refresh(key);
                    }
                })
                .catch((e) => {
                    if (!disposed) setListenerError(String(e));
                });
        };
        register(
            listenTwitchMetrics((snapshot) => {
                void client.cancelQueries({ queryKey: metricsKey }).then(() => {
                    if (!disposed) client.setQueryData(metricsKey, snapshot);
                });
            }),
            metricsKey,
        );
        register(
            listenStreamHistory(() => {
                if (!disposed) void refresh(historyKey);
            }),
            historyKey,
        );
        return () => {
            disposed = true;
            stops.forEach((fn) => fn());
        };
    }, [client, attempt]);
    const data = metrics.data,
        active = history.data?.active;
    const counts: [string, TwitchCount | undefined][] = [
        ["Zuschauer", data?.viewerCount],
        ["Follower", data?.followers],
        ["Subscriptions", data?.subscriptions],
        ["Chatter", data?.chatters],
    ];
    const newCount = (value: number | null | undefined): TwitchCount => ({
        value: value ?? null,
        at: null,
        error: history.isError ? String(history.error) : null,
    });
    const selected =
        statistic === "FollowerCount"
            ? counts[1]
            : statistic === "SubscriberCount"
              ? counts[2]
              : statistic === "ChatterCount"
                ? counts[3]
                : statistic === "NewFollowers"
                  ? ([
                        "Neue Follower",
                        newCount(
                            active?.FollowersKnown
                                ? active.FollowersGained
                                : null,
                        ),
                    ] as const)
                  : statistic === "NewSubscribers"
                    ? ([
                          "Neue Subscriptions",
                          newCount(active?.NewSubscriptions),
                      ] as const)
                    : counts[0];
    const selectedConnected = statistic.startsWith("New")
        ? Boolean(
              active &&
              active.ObservationAvailable !== false &&
              !history.isError,
          )
        : Boolean(data?.connected && !metrics.isError);
    const errors = [
        metrics.error,
        statistic.startsWith("New") ? history.error : null,
        listenerError,
        data?.channelError,
        ...counts.map(([, count]) => count?.error),
    ].filter(Boolean);
    const samples = (
        Array.isArray(active?.ViewerSamples)
            ? active.ViewerSamples.filter(
                  (sample) =>
                      sample &&
                      Number.isFinite(sample.ViewerCount) &&
                      sample.ViewerCount >= 0,
              )
            : []
    ).slice(-48);
    const max = Math.max(1, ...samples.map((s) => s.ViewerCount));
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Community</h2>
            {data && (data.title || data.category) && (
                <p>
                    {data.title} · {data.category}
                </p>
            )}
            <div className="text-xl">
                <Count
                    label={selected[0]}
                    count={selected[1]}
                    connected={selectedConnected}
                />
            </div>
            <div className="grid gap-2 sm:grid-cols-2">
                {counts
                    .filter(([label]) => label !== selected[0])
                    .map(([label, count]) => (
                        <Count
                            key={label}
                            label={label}
                            count={count}
                            connected={data?.connected && !metrics.isError}
                        />
                    ))}
            </div>
            {samples.length > 1 && (
                <svg
                    role="img"
                    aria-label="Zuschauerverlauf der aktuellen Sitzung"
                    viewBox="0 0 240 64"
                    className="w-full text-brand"
                >
                    <polyline
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="2"
                        points={samples
                            .map(
                                (s, i) =>
                                    `${(i * 240) / (samples.length - 1)},${60 - (s.ViewerCount * 56) / max}`,
                            )
                            .join(" ")}
                    />
                </svg>
            )}
            {!active && statistic.startsWith("New") && (
                <p className="text-muted">Keine aktive Sitzung</p>
            )}
            {errors.length > 0 && (
                <p role="alert" className="text-danger">
                    {[...new Set(errors.map(String))].join(" · ")}
                </p>
            )}
            <Button
                variant="ghost"
                onClick={() => {
                    if (listenerError) setAttempt((a) => a + 1);
                    void metrics.refetch();
                    void history.refetch();
                }}
            >
                Kennzahlen aktualisieren
            </Button>
        </Card>
    );
}
export function DashboardObsPreview({
    enabled,
    size,
}: {
    enabled: boolean;
    size: string;
}) {
    const [imageError, setImageError] = useState(false);
    const preview = useQuery({
        queryKey: ["dashboard-obs-preview"],
        queryFn: () =>
            tauriInvoke<{ url: string; width: number; height: number }>(
                "dashboard_obs_preview",
            ),
        enabled,
        refetchInterval: 2000,
        retry: false,
    });
    useEffect(() => setImageError(false), [preview.data]);
    return (
        <div className="space-y-2">
            {!enabled ? (
                <p className="text-muted">OBS-Vorschau nicht verbunden</p>
            ) : preview.isError ? (
                <p role="alert" className="text-danger">
                    {preview.error instanceof Error
                        ? preview.error.message
                        : String(preview.error)}
                </p>
            ) : preview.data ? (
                <img
                    src={preview.data.url}
                    alt="Aktuelle OBS-Szene"
                    onError={() => setImageError(true)}
                    style={{
                        aspectRatio: `${preview.data.width || 16}/${preview.data.height || 9}`,
                        maxWidth:
                            size === "Kompakt"
                                ? 200
                                : size === "Groß"
                                  ? 800
                                  : 400,
                    }}
                    className="w-full rounded border border-border object-contain"
                />
            ) : (
                <p className="text-muted">OBS-Vorschau wird geladen …</p>
            )}
            {enabled && imageError && (
                <p role="alert" className="text-danger">
                    OBS-Bild konnte nicht angezeigt werden.
                </p>
            )}
        </div>
    );
}
