import type { TwitchAction } from "../../lib/command-contract";
import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { listenTwitchEvents, tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { ChatCatalogStatusPanel } from "./ChatCatalogStatus";
import { TwitchChatMessage } from "./TwitchChatMessage";
import { TwitchEventTime } from "./TwitchEventFeed";
import { TwitchModeration, useModerationAction } from "./TwitchModeration";
type Event = {
    type: string;
    at: string;
    summary?: string;
    data: Record<string, string>;
};
export function TwitchChat({
    enabled,
    onSelectUser,
}: {
    enabled: boolean;
    onSelectUser?: (login: string) => void;
}) {
    const client = useQueryClient();
    const [message, setMessage] = useState("");
    const messageVersion = useRef(0);
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
    const action = useMutation({
        mutationFn: (action: TwitchAction) =>
            tauriInvoke("twitch_action", { action }),
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
    const selectUser = (
        next: (previous: { login: string; version: number }) => {
            login: string;
            version: number;
        },
    ) => {
        const value = next(moderationUser);
        if (onSelectUser) onSelectUser(value.login);
        else setModerationUser(value);
    };
    return (
        <div className="space-y-4">
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
                                        selectUser((previous) => ({
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
                        const sentVersion = messageVersion.current;
                        action.mutate(
                            { action: "send_chat", message },
                            {
                                onSuccess: () => {
                                    if (messageVersion.current === sentVersion)
                                        setMessage("");
                                },
                            },
                        );
                    }}
                >
                    <Input
                        aria-label="Chatnachricht"
                        maxLength={500}
                        value={message}
                        onChange={(e) => {
                            messageVersion.current++;
                            setMessage(e.target.value);
                        }}
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
            {!onSelectUser && moderationUser.login && (
                <TwitchModeration
                    enabled={enabled}
                    selectedUser={moderationUser.login}
                    selectionVersion={moderationUser.version}
                />
            )}
        </div>
    );
}
