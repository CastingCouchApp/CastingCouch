import type { TwitchAction, TwitchQuery } from "../../lib/command-contract";
import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listenTwitchEvents, tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { TwitchRewards } from "./TwitchRewards";
import { TwitchVotes } from "./TwitchVotes";
import { ChatCatalogStatusPanel } from "./ChatCatalogStatus";
import { TwitchChatMessage } from "./TwitchChatMessage";
import { TwitchEventFeed, TwitchEventTime } from "./TwitchEventFeed";
import { TwitchModeration, useModerationAction } from "./TwitchModeration";
import { TwitchGoals } from "./TwitchGoals";
type Event = {
    type: string;
    at: string;
    summary?: string;
    data: Record<string, string>;
};
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
    const [chatListenerError, setChatListenerError] = useState<string>();
    const [chatSubscriptionAttempt, setChatSubscriptionAttempt] = useState(0);
    const chatList = useRef<HTMLDivElement>(null);
    const [moderationUser, setModerationUser] = useState({
        login: "",
        version: 0,
    });
    const moderation = useModerationAction();
    const history = useQuery({
        queryKey: ["twitch-chat-history"],
        queryFn: () => tauriInvoke<{ events: Event[] }>("twitch_chat_feed"),
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
        let timer: ReturnType<typeof setTimeout> | undefined;
        setChatListenerError(undefined);
        const refresh = async () => {
            await client.cancelQueries({ queryKey: ["twitch-chat-history"] });
            if (!cancelled)
                await client.invalidateQueries({
                    queryKey: ["twitch-chat-history"],
                });
        };
        void listenTwitchEvents((event) => {
            if (
                cancelled ||
                typeof event.type !== "string" ||
                !event.type.startsWith("channel.chat.") ||
                timer
            )
                return;
            timer = setTimeout(() => {
                timer = undefined;
                void refresh();
            }, 150);
        })
            .then((fn) => {
                if (cancelled) fn();
                else {
                    unlisten = fn;
                    void refresh();
                }
            })
            .catch((error) => {
                if (!cancelled) setChatListenerError(String(error));
            });
        return () => {
            cancelled = true;
            unlisten?.();
            if (timer) clearTimeout(timer);
        };
    }, [client, chatSubscriptionAttempt]);
    useEffect(() => {
        if (chatList.current)
            chatList.current.scrollTop = chatList.current.scrollHeight;
    }, [history.data]);
    const select = (next: TwitchQuery) => {
        setAfter(undefined);
        setQuery(next);
    };
    const mutate = (value: TwitchAction) => action.mutate(value);
    return (
        <div className="grid gap-4 xl:grid-cols-2">
            <TwitchEventFeed />
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">Twitch-Chat</h2>
                <ChatCatalogStatusPanel enabled={enabled} />
                {history.isPending && <p role="status">Chat wird geladen …</p>}
                {(history.error || chatListenerError) && (
                    <p role="alert">
                        {[
                            chatListenerError,
                            history.error ? String(history.error) : undefined,
                        ]
                            .filter(Boolean)
                            .join(" · ")}
                    </p>
                )}
                <Button
                    variant="ghost"
                    disabled={history.isFetching}
                    onClick={() => {
                        if (chatListenerError)
                            setChatSubscriptionAttempt((value) => value + 1);
                        else void history.refetch();
                    }}
                >
                    Chat aktualisieren
                </Button>
                <div
                    ref={chatList}
                    aria-label="Twitch-Chatnachrichten"
                    className="max-h-80 overflow-auto space-y-2"
                >
                    {history.data?.events?.map((event, index) => (
                        <div
                            key={event.data.messageId ?? index}
                            className="flex gap-2 text-sm"
                        >
                            <TwitchEventTime at={event.at} />
                            <TwitchChatMessage
                                data={{
                                    ...event.data,
                                    text:
                                        event.data.text ?? event.summary ?? "",
                                }}
                            />
                            <Button
                                disabled={
                                    !enabled ||
                                    action.isPending ||
                                    moderation.isPending ||
                                    !event.data.messageId
                                }
                                variant="ghost"
                                onClick={() =>
                                    moderation.mutate({
                                        action: "delete_message",
                                        messageId: event.data.messageId,
                                    })
                                }
                            >
                                Löschen
                            </Button>
                            <Button
                                disabled={
                                    !enabled ||
                                    action.isPending ||
                                    moderation.isPending ||
                                    !event.data.userId
                                }
                                variant="ghost"
                                onClick={() =>
                                    moderation.mutate({
                                        action: "timeout",
                                        user: event.data.userId,
                                        byId: true,
                                        minutes: 10,
                                        reason: "Chat-Moderation",
                                    })
                                }
                            >
                                10 Min. Timeout
                            </Button>
                            {event.data.userLogin && (
                                <Button
                                    variant="ghost"
                                    onClick={() =>
                                        setModerationUser((previous) => ({
                                            login: event.data.userLogin,
                                            version: previous.version + 1,
                                        }))
                                    }
                                >
                                    Moderieren
                                </Button>
                            )}
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
                        disabled={
                            !enabled || action.isPending || moderation.isPending
                        }
                        variant="danger"
                        onClick={() => {
                            if (
                                window.confirm(
                                    "Den Twitch-Chat vollständig leeren?",
                                )
                            )
                                moderation.mutate({
                                    action: "clear_chat",
                                });
                        }}
                    >
                        Chat leeren
                    </Button>
                </div>
                {(action.error || webChat.error || moderation.error) && (
                    <p role="alert">
                        {String(
                            action.error ?? webChat.error ?? moderation.error,
                        )}
                    </p>
                )}
                {moderation.data?.message && (
                    <p role="status">{moderation.data.message}</p>
                )}
                {moderation.data?.warnings.map((warning, index) => (
                    <p role="alert" key={index}>
                        {warning}
                    </p>
                ))}
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
            <TwitchGoals enabled={enabled} />
            <TwitchModeration
                enabled={enabled}
                selectedUser={moderationUser.login}
                selectionVersion={moderationUser.version}
            />
            <TwitchVotes enabled={enabled} />
        </div>
    );
}
