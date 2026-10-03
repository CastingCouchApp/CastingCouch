import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
    FALLBACK_POLL_MS,
    listenTwitchEvents,
    tauriInvoke,
    type TwitchEventFeedSnapshot,
} from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";

const feedKey = ["twitch-event-feed"] as const;

export function TwitchEventTime({ at }: { at: string }) {
    const date = new Date(at);
    if (!Number.isFinite(date.getTime())) return <span>—</span>;
    return (
        <time
            dateTime={at}
            title={date.toLocaleString("de-DE")}
            className="shrink-0 text-muted tabular-nums"
        >
            {date.toLocaleTimeString("de-DE", {
                hour: "2-digit",
                minute: "2-digit",
                second: "2-digit",
            })}
        </time>
    );
}

export function TwitchEventFeed() {
    const client = useQueryClient();
    const [listenerError, setListenerError] = useState<string>();
    const [subscriptionAttempt, setSubscriptionAttempt] = useState(0);
    const feed = useQuery({
        queryKey: feedKey,
        queryFn: () =>
            tauriInvoke<TwitchEventFeedSnapshot>("twitch_event_feed"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    useEffect(() => {
        let disposed = false;
        let unlisten: (() => void) | undefined;
        let timer: ReturnType<typeof setTimeout> | undefined;
        setListenerError(undefined);
        const refresh = async () => {
            await client.cancelQueries({ queryKey: feedKey });
            if (!disposed)
                await client.invalidateQueries({ queryKey: feedKey });
        };
        void listenTwitchEvents((event) => {
            if (
                disposed ||
                event.source !== "twitch" ||
                event.type === "channel.chat.message" ||
                timer
            )
                return;
            timer = setTimeout(() => {
                timer = undefined;
                void refresh();
            }, 150);
        })
            .then((fn) => {
                if (disposed) {
                    fn();
                    return;
                }
                unlisten = fn;
                // Recover entries received between the initial query and listener registration.
                void refresh();
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            unlisten?.();
            if (timer) clearTimeout(timer);
        };
    }, [client, subscriptionAttempt]);
    const error = [listenerError, feed.error ? String(feed.error) : undefined]
        .filter(Boolean)
        .join(" · ");
    return (
        <Card className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="text-lg font-semibold">Twitch-Ereignisse</h2>
                <Button
                    variant="ghost"
                    disabled={feed.isFetching}
                    onClick={() => {
                        if (listenerError)
                            setSubscriptionAttempt((value) => value + 1);
                        else void feed.refetch();
                    }}
                >
                    Ereignisse aktualisieren
                </Button>
            </div>
            {feed.isPending && <p role="status">Ereignisse werden geladen …</p>}
            {error && (
                <p role="alert" className="whitespace-pre-wrap break-words">
                    {error}
                </p>
            )}
            {feed.data?.events.length === 0 && (
                <p className="text-muted">
                    Noch keine Twitch-Ereignisse empfangen.
                </p>
            )}
            <ol
                aria-label="Twitch-Ereignisse"
                className="max-h-80 overflow-auto space-y-2"
            >
                {feed.data?.events.map((event, index) => (
                    <li
                        key={`${event.at}-${index}`}
                        className="flex gap-2 text-sm"
                    >
                        <TwitchEventTime at={event.at} />
                        <span
                            title={event.type}
                            className={`min-w-0 whitespace-pre-wrap break-words ${["subscription.warning", "revocation"].includes(event.type) ? "text-warning" : ""}`}
                        >
                            {event.summary || event.type}
                        </span>
                    </li>
                ))}
            </ol>
        </Card>
    );
}
