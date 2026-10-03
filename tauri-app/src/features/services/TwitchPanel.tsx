import type { TwitchAction, TwitchQuery } from "../../lib/command-contract";
import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listenTwitchEvents, tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { TwitchRewards } from "./TwitchRewards";
import { TwitchVotes } from "./TwitchVotes";
import { ChatCatalogStatusPanel } from "./ChatCatalogStatus";
import { TwitchChatMessage } from "./TwitchChatMessage";
type Event = { type: string; summary?: string; data: Record<string, string> };
type Item = {
    id: string;
    title?: string;
    name?: string;
    display_name?: string;
    user_name?: string;
    broadcaster_name?: string;
    broadcaster_id?: string;
    user_id?: string;
    game_name?: string;
    status?: string;
    outcomes?: { id: string; title: string }[];
};
type Result = {
    data: Item[];
    total?: number;
    pagination?: { cursor?: string };
};
export function TwitchPanel({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const [message, setMessage] = useState("");
    const [title, setTitle] = useState("");
    const [category, setCategory] = useState("");
    const [search, setSearch] = useState("");
    const [query, setQuery] = useState<TwitchQuery>({
        query: "channel",
    });
    const [after, setAfter] = useState<string>();
    const history = useQuery({
        queryKey: ["twitch-chat-history"],
        queryFn: () => tauriInvoke<{ events: Event[] }>("chat_history"),
        refetchInterval: 15000,
    });
    const result = useQuery({
        queryKey: ["twitch-query", query, after],
        queryFn: () =>
            tauriInvoke<Result>("twitch_query", {
                query,
                after: after ?? null,
            }),
        enabled,
    });
    const action = useMutation({
        mutationFn: (action: TwitchAction) =>
            tauriInvoke("twitch_action", { action }),
        onSuccess: () => {
            void client.invalidateQueries({ queryKey: ["twitch-query"] });
        },
    });
    const webChat = useMutation({
        mutationFn: () => tauriInvoke("open_twitch_chat"),
    });
    useEffect(() => {
        let cancelled = false;
        let unlisten: (() => void) | undefined;
        void listenTwitchEvents(
            () =>
                void client.invalidateQueries({
                    queryKey: ["twitch-chat-history"],
                }),
        ).then((fn) => {
            if (cancelled) fn();
            else unlisten = fn;
        });
        return () => {
            cancelled = true;
            unlisten?.();
        };
    }, [client]);
    const select = (next: TwitchQuery) => {
        setAfter(undefined);
        setQuery(next);
    };
    const mutate = (value: TwitchAction) => action.mutate(value);
    return (
        <div className="grid gap-4 xl:grid-cols-2">
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">Twitch-Chat</h2>
                <ChatCatalogStatusPanel enabled={enabled} />
                <div className="max-h-80 overflow-auto space-y-2">
                    {history.data?.events?.map((event, index) => (
                        <div
                            key={event.data.messageId ?? index}
                            className="flex gap-2 text-sm"
                        >
                            <TwitchChatMessage
                                data={{
                                    ...event.data,
                                    text:
                                        event.data.text ?? event.summary ?? "",
                                }}
                            />
                            <Button
                                disabled={!enabled || action.isPending}
                                variant="ghost"
                                onClick={() =>
                                    mutate({
                                        action: "delete_chat",
                                        messageId: event.data.messageId,
                                    })
                                }
                            >
                                Löschen
                            </Button>
                            <Button
                                disabled={!enabled || action.isPending}
                                variant="ghost"
                                onClick={() =>
                                    mutate({
                                        action: "ban",
                                        id: event.data.userId,
                                        duration: 600,
                                        reason: "Chat-Moderation",
                                    })
                                }
                            >
                                10 Min. Timeout
                            </Button>
                        </div>
                    ))}
                </div>
                <form
                    className="flex gap-2"
                    onSubmit={(e) => {
                        e.preventDefault();
                        action.mutate(
                            { action: "send_chat", message },
                            { onSuccess: () => setMessage("") },
                        );
                    }}
                >
                    <Input
                        aria-label="Chatnachricht"
                        maxLength={500}
                        value={message}
                        onChange={(e) => setMessage(e.target.value)}
                    />
                    <Button
                        type="submit"
                        disabled={
                            !enabled || !message.trim() || action.isPending
                        }
                    >
                        Senden
                    </Button>
                </form>
                <div className="flex gap-2">
                    <Button
                        disabled={!enabled}
                        onClick={() => webChat.mutate()}
                    >
                        Twitch-Webchat öffnen
                    </Button>
                    <Button
                        disabled={!enabled || action.isPending}
                        variant="danger"
                        onClick={() => {
                            if (
                                window.confirm(
                                    "Den Twitch-Chat vollständig leeren?",
                                )
                            )
                                mutate({
                                    action: "delete_chat",
                                    messageId: null,
                                });
                        }}
                    >
                        Chat leeren
                    </Button>
                </div>
                {(action.error || webChat.error) && (
                    <p role="alert">{String(action.error ?? webChat.error)}</p>
                )}
            </Card>
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">Twitch-Kanal</h2>
                <label className="block">
                    Streamtitel
                    <Input
                        value={title}
                        onChange={(e) => setTitle(e.target.value)}
                    />
                </label>
                <label className="block">
                    Kategorie-ID
                    <Input
                        value={category}
                        onChange={(e) => setCategory(e.target.value)}
                    />
                </label>
                <Button
                    disabled={!enabled || action.isPending || !title.trim()}
                    onClick={() =>
                        mutate({
                            action: "channel",
                            title,
                            categoryId: category,
                        })
                    }
                >
                    Kanal aktualisieren
                </Button>
                <form
                    className="flex gap-2"
                    onSubmit={(e) => {
                        e.preventDefault();
                        select({ query: "search_categories", text: search });
                    }}
                >
                    <Input
                        aria-label="Twitch Suche"
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                    />
                    <Button type="submit" disabled={!enabled}>
                        Kategorie suchen
                    </Button>
                    <Button
                        disabled={!enabled}
                        onClick={() =>
                            select({ query: "search_channels", text: search })
                        }
                    >
                        Kanal suchen
                    </Button>
                </form>
                <div className="flex flex-wrap gap-2">
                    {(
                        [
                            ["channel", "Kanal"],
                            ["followers", "Follower"],
                            ["subscriptions", "Abos"],
                            ["chatters", "Chatter"],
                            ["followed_channels", "Gefolgte Kanäle"],
                            ["followed_streams", "Live-Kanäle"],
                        ] as const
                    ).map(([query, label]) => (
                        <Button
                            disabled={!enabled}
                            key={query}
                            onClick={() => select({ query })}
                        >
                            {label}
                        </Button>
                    ))}
                </div>
                {result.data?.total !== undefined && (
                    <p>Gesamt: {result.data.total}</p>
                )}
                <ul className="max-h-80 overflow-auto divide-y divide-border">
                    {result.data?.data?.map((item, index) => (
                        <li className="py-2 space-y-1" key={item.id ?? index}>
                            <span>
                                {item.title ??
                                    item.name ??
                                    item.display_name ??
                                    item.user_name ??
                                    item.broadcaster_name ??
                                    item.id}{" "}
                                {item.status}
                            </span>
                            {query.query === "search_categories" && (
                                <Button onClick={() => setCategory(item.id)}>
                                    Auswählen
                                </Button>
                            )}
                            {[
                                "search_channels",
                                "followed_channels",
                                "followed_streams",
                            ].includes(query.query) && (
                                <Button
                                    disabled={action.isPending}
                                    onClick={() => {
                                        if (
                                            window.confirm(
                                                `Raid zu ${item.display_name ?? item.broadcaster_name ?? item.user_name} starten?`,
                                            )
                                        )
                                            mutate({
                                                action: "raid",
                                                id:
                                                    item.broadcaster_id ??
                                                    item.user_id ??
                                                    item.id,
                                            });
                                    }}
                                >
                                    Raid starten
                                </Button>
                            )}
                        </li>
                    ))}
                </ul>
                {result.error && <p role="alert">{String(result.error)}</p>}
                {result.data?.pagination?.cursor && (
                    <Button
                        onClick={() =>
                            setAfter(result.data?.pagination?.cursor)
                        }
                    >
                        Nächste Seite
                    </Button>
                )}
            </Card>
            <TwitchRewards enabled={enabled} />
            <TwitchVotes enabled={enabled} />
        </div>
    );
}
