import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listenTwitchEvents, tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
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
    const [query, setQuery] = useState<Record<string, string>>({
        query: "channel",
    });
    const [after, setAfter] = useState<string>();
    const [rewardTitle, setRewardTitle] = useState("");
    const [cost, setCost] = useState(100);
    const [question, setQuestion] = useState("");
    const [choices, setChoices] = useState("Ja\nNein");
    const [duration, setDuration] = useState(60);
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
        mutationFn: (action: Record<string, unknown>) =>
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
    const select = (next: Record<string, string>) => {
        setAfter(undefined);
        setQuery(next);
    };
    const mutate = (value: Record<string, unknown>) => action.mutate(value);
    return (
        <div className="grid gap-4 xl:grid-cols-2">
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">Twitch-Chat</h2>
                <div className="max-h-80 overflow-auto space-y-2">
                    {history.data?.events?.map((event, index) => (
                        <div
                            key={event.data.messageId ?? index}
                            className="flex gap-2 text-sm"
                        >
                            <strong>
                                {event.data.userName ?? event.data.userLogin}
                            </strong>
                            <span className="flex-1">
                                {event.data.text ?? event.summary}
                            </span>
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
                    {[
                        ["channel", "Kanal"],
                        ["followers", "Follower"],
                        ["subscriptions", "Abos"],
                        ["chatters", "Chatter"],
                        ["followed_channels", "Gefolgte Kanäle"],
                        ["followed_streams", "Live-Kanäle"],
                        ["rewards", "Rewards"],
                        ["polls", "Umfragen"],
                        ["predictions", "Vorhersagen"],
                    ].map(([query, label]) => (
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
                            {query.query === "rewards" && (
                                <Button
                                    onClick={() =>
                                        select({
                                            query: "redemptions",
                                            rewardId: item.id,
                                        })
                                    }
                                >
                                    Einlösungen
                                </Button>
                            )}
                            {query.query === "redemptions" && (
                                <>
                                    <Button
                                        onClick={() =>
                                            mutate({
                                                action: "update_redemption",
                                                rewardId: query.rewardId,
                                                id: item.id,
                                                status: "FULFILLED",
                                            })
                                        }
                                    >
                                        Erfüllt
                                    </Button>
                                    <Button
                                        onClick={() =>
                                            mutate({
                                                action: "update_redemption",
                                                rewardId: query.rewardId,
                                                id: item.id,
                                                status: "CANCELED",
                                            })
                                        }
                                    >
                                        Erstatten
                                    </Button>
                                </>
                            )}
                            {query.query === "polls" &&
                                item.status === "ACTIVE" && (
                                    <Button
                                        onClick={() =>
                                            mutate({
                                                action: "end_poll",
                                                id: item.id,
                                                status: "TERMINATED",
                                            })
                                        }
                                    >
                                        Beenden
                                    </Button>
                                )}
                            {query.query === "predictions" &&
                                ["ACTIVE", "LOCKED"].includes(
                                    item.status ?? "",
                                ) && (
                                    <div className="flex gap-2">
                                        {item.outcomes?.map((outcome) => (
                                            <Button
                                                key={outcome.id}
                                                onClick={() =>
                                                    mutate({
                                                        action: "end_prediction",
                                                        id: item.id,
                                                        status: "RESOLVED",
                                                        winningOutcomeId:
                                                            outcome.id,
                                                    })
                                                }
                                            >
                                                {outcome.title} gewinnt
                                            </Button>
                                        ))}
                                        <Button
                                            onClick={() =>
                                                mutate({
                                                    action: "end_prediction",
                                                    id: item.id,
                                                    status: "CANCELED",
                                                    winningOutcomeId: null,
                                                })
                                            }
                                        >
                                            Abbrechen
                                        </Button>
                                    </div>
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
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">Channel-Points-Reward</h2>
                <label className="block">
                    Titel
                    <Input
                        value={rewardTitle}
                        onChange={(e) => setRewardTitle(e.target.value)}
                    />
                </label>
                <label className="block">
                    Punkte
                    <Input
                        type="number"
                        min="1"
                        value={cost}
                        onChange={(e) => setCost(Number(e.target.value))}
                    />
                </label>
                <Button
                    disabled={
                        !enabled || !rewardTitle.trim() || action.isPending
                    }
                    onClick={() =>
                        mutate({
                            action: "create_reward",
                            title: rewardTitle,
                            cost,
                            prompt: "",
                        })
                    }
                >
                    Reward anlegen
                </Button>
            </Card>
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">
                    Umfrage oder Vorhersage
                </h2>
                <label className="block">
                    Frage
                    <Input
                        value={question}
                        onChange={(e) => setQuestion(e.target.value)}
                    />
                </label>
                <label className="block">
                    Antworten (eine pro Zeile)
                    <textarea
                        className="w-full bg-panel border border-border rounded p-2"
                        value={choices}
                        onChange={(e) => setChoices(e.target.value)}
                    />
                </label>
                <label className="block">
                    Dauer in Sekunden
                    <Input
                        type="number"
                        min="30"
                        value={duration}
                        onChange={(e) => setDuration(Number(e.target.value))}
                    />
                </label>
                <div className="flex gap-2">
                    <Button
                        disabled={
                            !enabled || !question.trim() || action.isPending
                        }
                        onClick={() =>
                            mutate({
                                action: "create_poll",
                                title: question,
                                choices: choices.split("\n").filter(Boolean),
                                duration,
                            })
                        }
                    >
                        Umfrage starten
                    </Button>
                    <Button
                        disabled={
                            !enabled || !question.trim() || action.isPending
                        }
                        onClick={() =>
                            mutate({
                                action: "create_prediction",
                                title: question,
                                outcomes: choices.split("\n").filter(Boolean),
                                window: duration,
                            })
                        }
                    >
                        Vorhersage starten
                    </Button>
                </div>
            </Card>
        </div>
    );
}
