import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import type { TwitchAction } from "../../lib/command-contract";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { useTwitchRefresh } from "./useTwitchRefresh";

type Reward = {
    id: string;
    title: string;
    cost: number;
    prompt?: string;
    is_enabled?: boolean;
    is_paused?: boolean;
    is_user_input_required?: boolean;
    background_color?: string;
};
type Redemption = {
    id: string;
    user_name: string;
    user_input: string;
    status: string;
    redeemed_at?: string;
};
type Result<T> = { data: T[]; pagination?: { cursor?: string } };
const initial = {
    title: "",
    cost: 100,
    prompt: "",
    is_enabled: true,
    is_paused: false,
    is_user_input_required: false,
    background_color: "#9146FF",
};
type Draft = typeof initial & { id?: string };
export function TwitchRewards({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const [draft, setDraft] = useState<Draft>(initial);
    const [selected, setSelected] = useState<Reward>();
    const [status, setStatus] = useState("UNFULFILLED");
    const [after, setAfter] = useState<string>();
    const refreshError = useTwitchRefresh(
        enabled,
        "channel.channel_points_custom_reward",
        "twitch-rewards",
    );
    const rewards = useQuery({
        queryKey: ["twitch-rewards", "list"],
        enabled,
        refetchInterval: enabled ? 15000 : false,
        queryFn: () =>
            tauriInvoke<Result<Reward>>("twitch_query", {
                query: { query: "rewards" },
            }),
    });
    const redemptions = useQuery({
        queryKey: [
            "twitch-rewards",
            "redemptions",
            selected?.id,
            status,
            after,
        ],
        enabled: enabled && !!selected,
        refetchInterval: enabled ? 15000 : false,
        queryFn: () =>
            tauriInvoke<Result<Redemption>>("twitch_query", {
                query: { query: "redemptions", rewardId: selected!.id, status },
                after: after ?? null,
            }),
    });
    const action = useMutation({
        mutationFn: (action: TwitchAction) =>
            tauriInvoke("twitch_action", { action }),
        onSuccess: () => {
            void client.invalidateQueries({ queryKey: ["twitch-rewards"] });
        },
    });
    const busy = !enabled || action.isPending;
    const valid =
        draft.title.trim().length > 0 &&
        draft.title.trim().length <= 45 &&
        Number.isInteger(draft.cost) &&
        draft.cost >= 1 &&
        draft.prompt.length <= 200;
    const edit = (reward: Reward) =>
        setDraft({
            ...initial,
            ...reward,
            prompt: reward.prompt ?? "",
            is_enabled: reward.is_enabled ?? true,
            is_paused: reward.is_paused ?? false,
            is_user_input_required: reward.is_user_input_required ?? false,
            background_color:
                reward.background_color ?? initial.background_color,
        });
    return (
        <Card className="space-y-3">
            <section className="space-y-3">
                <h2 className="text-lg font-semibold">Channel Points</h2>
                <p className="text-sm text-muted">
                    Bearbeiten, Löschen und Einlösungen bearbeiten sind für
                    Rewards dieser Twitch-App verfügbar. Twitch meldet fehlende
                    Rechte.
                </p>
                <Button
                    disabled={!enabled || rewards.isFetching}
                    onClick={() => {
                        void client.invalidateQueries({
                            queryKey: ["twitch-rewards"],
                        });
                    }}
                >
                    Rewards aktualisieren
                </Button>
                <ul className="max-h-80 overflow-auto divide-y divide-border">
                    {rewards.data?.data?.map((reward) => (
                        <li key={reward.id} className="py-2 space-y-2">
                            <p>
                                <strong>{reward.title}</strong> · {reward.cost}{" "}
                                Punkte ·{" "}
                                {reward.is_enabled === false
                                    ? "Deaktiviert"
                                    : reward.is_paused
                                      ? "Pausiert"
                                      : "Aktiv"}
                            </p>
                            <p className="text-sm">{reward.prompt}</p>
                            <div className="flex flex-wrap gap-2">
                                <Button
                                    disabled={busy}
                                    onClick={() => edit(reward)}
                                >
                                    Bearbeiten
                                </Button>
                                <Button
                                    disabled={busy}
                                    onClick={() =>
                                        action.mutate({
                                            action: "update_reward",
                                            id: reward.id,
                                            isPaused: !reward.is_paused,
                                        })
                                    }
                                >
                                    {reward.is_paused
                                        ? "Fortsetzen"
                                        : "Pausieren"}
                                </Button>
                                <Button
                                    disabled={busy}
                                    onClick={() => {
                                        setSelected(reward);
                                        setStatus("UNFULFILLED");
                                        setAfter(undefined);
                                    }}
                                >
                                    Einlösungen
                                </Button>
                                <Button
                                    disabled={busy}
                                    variant="danger"
                                    onClick={() => {
                                        if (
                                            window.confirm(
                                                `Reward „${reward.title}“ löschen? Offene Einlösungen werden von Twitch als erfüllt markiert.`,
                                            )
                                        )
                                            action.mutate(
                                                {
                                                    action: "delete_reward",
                                                    id: reward.id,
                                                },
                                                {
                                                    onSuccess: () => {
                                                        if (
                                                            draft.id ===
                                                            reward.id
                                                        )
                                                            setDraft(initial);
                                                        if (
                                                            selected?.id ===
                                                            reward.id
                                                        )
                                                            setSelected(
                                                                undefined,
                                                            );
                                                    },
                                                },
                                            );
                                    }}
                                >
                                    Reward löschen
                                </Button>
                            </div>
                        </li>
                    ))}
                </ul>
                {enabled && rewards.data?.data?.length === 0 && (
                    <p>Noch keine Rewards.</p>
                )}
                <form
                    className="space-y-3"
                    onSubmit={(e) => {
                        e.preventDefault();
                        if (!valid || busy) return;
                        const fields = {
                            title: draft.title,
                            cost: draft.cost,
                            prompt: draft.prompt,
                            isEnabled: draft.is_enabled,
                            isUserInputRequired: draft.is_user_input_required,
                            backgroundColor: draft.background_color,
                        };
                        action.mutate(
                            draft.id
                                ? {
                                      action: "update_reward",
                                      id: draft.id,
                                      ...fields,
                                      isPaused: draft.is_paused,
                                  }
                                : { action: "create_reward", ...fields },
                            { onSuccess: () => setDraft(initial) },
                        );
                    }}
                >
                    <h3 className="font-semibold">
                        {draft.id ? "Reward bearbeiten" : "Reward anlegen"}
                    </h3>
                    <label className="block">
                        Reward-Titel
                        <Input
                            maxLength={45}
                            value={draft.title}
                            onChange={(e) =>
                                setDraft({ ...draft, title: e.target.value })
                            }
                        />
                    </label>
                    <label className="block">
                        Punkte
                        <Input
                            type="number"
                            min={1}
                            step={1}
                            value={draft.cost}
                            onChange={(e) =>
                                setDraft({
                                    ...draft,
                                    cost: Number(e.target.value),
                                })
                            }
                        />
                    </label>
                    <label className="block">
                        Beschreibung
                        <Input
                            maxLength={200}
                            value={draft.prompt}
                            onChange={(e) =>
                                setDraft({ ...draft, prompt: e.target.value })
                            }
                        />
                    </label>
                    <label className="block">
                        Reward-Farbe
                        <Input
                            type="color"
                            value={draft.background_color}
                            onChange={(e) =>
                                setDraft({
                                    ...draft,
                                    background_color: e.target.value,
                                })
                            }
                        />
                    </label>
                    <div className="flex flex-wrap gap-4">
                        {(
                            [
                                ["is_enabled", "Aktiviert"],
                                [
                                    "is_user_input_required",
                                    "Zuschauereingabe erforderlich",
                                ],
                                ...(draft.id
                                    ? [["is_paused", "Pausiert"]]
                                    : []),
                            ] as [
                                (
                                    | "is_enabled"
                                    | "is_user_input_required"
                                    | "is_paused"
                                ),
                                string,
                            ][]
                        ).map(([key, label]) => (
                            <label key={key} className="flex gap-2">
                                <input
                                    type="checkbox"
                                    checked={draft[key]}
                                    onChange={(e) =>
                                        setDraft({
                                            ...draft,
                                            [key]: e.target.checked,
                                        })
                                    }
                                />
                                {label}
                            </label>
                        ))}
                    </div>
                    <div className="flex gap-2">
                        <Button type="submit" disabled={busy || !valid}>
                            {draft.id ? "Reward speichern" : "Reward anlegen"}
                        </Button>
                        {draft.id && (
                            <Button
                                disabled={action.isPending}
                                onClick={() => setDraft(initial)}
                            >
                                Bearbeitung abbrechen
                            </Button>
                        )}
                    </div>
                </form>
                {selected && (
                    <section className="space-y-2">
                        <h3 className="font-semibold">
                            Einlösungen: {selected.title}
                        </h3>
                        <label className="block">
                            Einlösungsstatus
                            <select
                                className="bg-panel border border-border rounded p-2 ml-2"
                                value={status}
                                onChange={(e) => {
                                    setStatus(e.target.value);
                                    setAfter(undefined);
                                }}
                            >
                                <option value="UNFULFILLED">Offen</option>
                                <option value="FULFILLED">Erfüllt</option>
                                <option value="CANCELED">Erstattet</option>
                            </select>
                        </label>
                        <ul>
                            {redemptions.data?.data?.map((item) => (
                                <li
                                    key={item.id}
                                    className="space-y-1 border-b border-border py-2"
                                >
                                    <strong>{item.user_name}</strong>
                                    <p>{item.user_input}</p>
                                    <p className="text-sm text-muted">
                                        {item.status}{" "}
                                        {item.redeemed_at &&
                                            new Date(
                                                item.redeemed_at,
                                            ).toLocaleString()}
                                    </p>
                                    {item.status === "UNFULFILLED" && (
                                        <div className="flex gap-2">
                                            <Button
                                                disabled={busy}
                                                onClick={() =>
                                                    action.mutate({
                                                        action: "update_redemption",
                                                        rewardId: selected.id,
                                                        id: item.id,
                                                        status: "FULFILLED",
                                                    })
                                                }
                                            >
                                                Erfüllt
                                            </Button>
                                            <Button
                                                disabled={busy}
                                                onClick={() =>
                                                    action.mutate({
                                                        action: "update_redemption",
                                                        rewardId: selected.id,
                                                        id: item.id,
                                                        status: "CANCELED",
                                                    })
                                                }
                                            >
                                                Erstatten
                                            </Button>
                                        </div>
                                    )}
                                </li>
                            ))}
                        </ul>
                        {redemptions.data?.data?.length === 0 && (
                            <p>Keine Einlösungen für diesen Status.</p>
                        )}
                        <div className="flex gap-2">
                            {after && (
                                <Button
                                    disabled={redemptions.isFetching}
                                    onClick={() => setAfter(undefined)}
                                >
                                    Erste Einlösungen
                                </Button>
                            )}
                            {redemptions.data?.pagination?.cursor && (
                                <Button
                                    disabled={redemptions.isFetching}
                                    onClick={() =>
                                        setAfter(
                                            redemptions.data?.pagination
                                                ?.cursor,
                                        )
                                    }
                                >
                                    Nächste Einlösungen
                                </Button>
                            )}
                        </div>
                    </section>
                )}
                {[
                    rewards.error,
                    selected && redemptions.error,
                    action.error,
                    refreshError,
                ]
                    .filter(Boolean)
                    .map((error, i) => (
                        <p key={i} role="alert" className="text-danger">
                            {String(error)}
                        </p>
                    ))}
            </section>
        </Card>
    );
}
