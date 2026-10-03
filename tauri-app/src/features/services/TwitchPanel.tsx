import type { TwitchAction, TwitchQuery } from "../../lib/command-contract";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { TwitchRewards } from "./TwitchRewards";
import { TwitchVotes } from "./TwitchVotes";
import { TwitchEventFeed } from "./TwitchEventFeed";
import { TwitchModeration } from "./TwitchModeration";
import { TwitchGoals } from "./TwitchGoals";
import { TwitchChat } from "./TwitchChat";
import { TwitchRaids } from "./TwitchRaids";
type Item = {
    id: string;
    title?: string;
    name?: string;
    display_name?: string;
    user_name?: string;
    broadcaster_name?: string;
    broadcaster_id?: string;
    broadcaster_login?: string;
    user_login?: string;
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
    const [title, setTitle] = useState("");
    const [category, setCategory] = useState("");
    const [search, setSearch] = useState("");
    const [query, setQuery] = useState<TwitchQuery>({
        query: "channel",
    });
    const [after, setAfter] = useState<string>();
    const [moderationUser, setModerationUser] = useState({
        login: "",
        version: 0,
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
    const selectRaid = useMutation({
        mutationFn: (login: string) =>
            tauriInvoke("select_twitch_raid_target", { login }),
        onSuccess: () =>
            client.invalidateQueries({ queryKey: ["twitch-raid-settings"] }),
    });
    const select = (next: TwitchQuery) => {
        setAfter(undefined);
        setQuery(next);
    };
    const mutate = (value: TwitchAction) => action.mutate(value);
    return (
        <div className="grid gap-4 xl:grid-cols-2">
            <TwitchEventFeed />
            <TwitchChat
                enabled={enabled}
                onSelectUser={(login) =>
                    setModerationUser((previous) => ({
                        login,
                        version: previous.version + 1,
                    }))
                }
            />
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
                                    disabled={
                                        selectRaid.isPending ||
                                        !(
                                            item.broadcaster_login ??
                                            item.user_login
                                        )
                                    }
                                    onClick={() => {
                                        selectRaid.mutate(
                                            item.broadcaster_login ??
                                                item.user_login ??
                                                "",
                                        );
                                    }}
                                >
                                    Als Raid-Ziel wählen
                                </Button>
                            )}
                        </li>
                    ))}
                </ul>
                {result.error && <p role="alert">{String(result.error)}</p>}
                {selectRaid.error && (
                    <p role="alert">{String(selectRaid.error)}</p>
                )}
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
            <TwitchRaids enabled={enabled} />
            <TwitchModeration
                enabled={enabled}
                selectedUser={moderationUser.login}
                selectionVersion={moderationUser.version}
            />
            <TwitchVotes enabled={enabled} />
        </div>
    );
}
