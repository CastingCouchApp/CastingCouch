import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import type { TwitchAction } from "../../lib/command-contract";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { useTwitchRefresh } from "./useTwitchRefresh";

type Vote = {
    id: string;
    title: string;
    status: string;
    winning_outcome_id?: string;
    choices?: { id: string; title: string; votes: number }[];
    outcomes?: {
        id: string;
        title: string;
        users: number;
        channel_points: number;
    }[];
    ended_at?: string;
};
export function TwitchVotes({ enabled }: { enabled: boolean }) {
    return (
        <>
            <VoteManager enabled={enabled} prediction={false} />
            <VoteManager enabled={enabled} prediction />
        </>
    );
}
function VoteManager({
    enabled,
    prediction,
}: {
    enabled: boolean;
    prediction: boolean;
}) {
    const kind = prediction ? "predictions" : "polls";
    const label = prediction ? "Vorhersage" : "Umfrage";
    const client = useQueryClient();
    const [title, setTitle] = useState("");
    const [answers, setAnswers] = useState("Ja\nNein");
    const [duration, setDuration] = useState(60);
    const [after, setAfter] = useState<string>();
    const refreshError = useTwitchRefresh(
        enabled,
        prediction ? "channel.prediction." : "channel.poll.",
        `twitch-${kind}`,
    );
    const result = useQuery({
        queryKey: [`twitch-${kind}`, after],
        enabled,
        refetchInterval: enabled ? 15000 : false,
        queryFn: () =>
            tauriInvoke<{ data: Vote[]; pagination?: { cursor?: string } }>(
                "twitch_query",
                { query: { query: kind }, after: after ?? null },
            ),
    });
    const action = useMutation({
        mutationFn: (action: TwitchAction) =>
            tauriInvoke("twitch_action", { action }),
        onSuccess: () => {
            void client.invalidateQueries({ queryKey: [`twitch-${kind}`] });
        },
    });
    const options = answers
        .split("\n")
        .map((s) => s.trim())
        .filter(Boolean);
    const busy = !enabled || action.isPending;
    const valid =
        !!title.trim() &&
        title.trim().length <= (prediction ? 45 : 60) &&
        options.length >= 2 &&
        options.length <= (prediction ? 10 : 5) &&
        options.every((s) => Array.from(s).length <= 25) &&
        Number.isInteger(duration) &&
        duration >= (prediction ? 30 : 15) &&
        duration <= 1800;
    const hasActive = result.data?.data?.some((item) =>
        prediction
            ? ["ACTIVE", "LOCKED"].includes(item.status)
            : item.status === "ACTIVE",
    );
    return (
        <Card>
            <section className="space-y-3">
                <h2 className="text-lg font-semibold">
                    {prediction ? "Vorhersagen" : "Umfragen"}
                </h2>
                <Button
                    disabled={!enabled || result.isFetching}
                    onClick={() => void result.refetch()}
                >
                    Aktualisieren
                </Button>
                <ul className="max-h-96 overflow-auto divide-y divide-border">
                    {result.data?.data?.map((item) => (
                        <li key={item.id} className="space-y-2 py-3">
                            <p>
                                <strong>{item.title}</strong> · {item.status}
                            </p>
                            <ul className="text-sm">
                                {item.choices?.map((choice) => (
                                    <li key={choice.id}>
                                        {choice.title} · {choice.votes} Stimmen
                                    </li>
                                ))}
                                {item.outcomes?.map((outcome) => (
                                    <li key={outcome.id}>
                                        {outcome.title} · {outcome.users}{" "}
                                        Teilnehmer · {outcome.channel_points}{" "}
                                        Punkte{" "}
                                        {outcome.id ===
                                            item.winning_outcome_id &&
                                            "· Gewinner"}
                                    </li>
                                ))}
                            </ul>
                            {!prediction && item.status === "ACTIVE" && (
                                <Button
                                    disabled={busy}
                                    onClick={() =>
                                        action.mutate({
                                            action: "end_poll",
                                            id: item.id,
                                            status: "TERMINATED",
                                        })
                                    }
                                >
                                    Umfrage beenden
                                </Button>
                            )}
                            {!prediction &&
                                ["ACTIVE", "COMPLETED", "TERMINATED"].includes(
                                    item.status,
                                ) && (
                                    <Button
                                        disabled={busy}
                                        onClick={() => {
                                            if (
                                                window.confirm(
                                                    "Diese Umfrage archivieren?",
                                                )
                                            )
                                                action.mutate({
                                                    action: "end_poll",
                                                    id: item.id,
                                                    status: "ARCHIVED",
                                                });
                                        }}
                                    >
                                        Umfrage archivieren
                                    </Button>
                                )}
                            {prediction &&
                                ["ACTIVE", "LOCKED"].includes(item.status) && (
                                    <div className="flex flex-wrap gap-2">
                                        {item.status === "ACTIVE" && (
                                            <Button
                                                disabled={busy}
                                                onClick={() =>
                                                    action.mutate({
                                                        action: "end_prediction",
                                                        id: item.id,
                                                        status: "LOCKED",
                                                        winningOutcomeId: null,
                                                    })
                                                }
                                            >
                                                Vorhersage sperren
                                            </Button>
                                        )}
                                        {item.outcomes?.map((outcome) => (
                                            <Button
                                                key={outcome.id}
                                                disabled={busy}
                                                onClick={() => {
                                                    if (
                                                        window.confirm(
                                                            `„${outcome.title}“ als Gewinner auswählen?`,
                                                        )
                                                    )
                                                        action.mutate({
                                                            action: "end_prediction",
                                                            id: item.id,
                                                            status: "RESOLVED",
                                                            winningOutcomeId:
                                                                outcome.id,
                                                        });
                                                }}
                                            >
                                                {outcome.title} gewinnt
                                            </Button>
                                        ))}
                                        <Button
                                            disabled={busy}
                                            variant="danger"
                                            onClick={() => {
                                                if (
                                                    window.confirm(
                                                        "Vorhersage abbrechen und Punkte erstatten?",
                                                    )
                                                )
                                                    action.mutate({
                                                        action: "end_prediction",
                                                        id: item.id,
                                                        status: "CANCELED",
                                                        winningOutcomeId: null,
                                                    });
                                            }}
                                        >
                                            Vorhersage abbrechen
                                        </Button>
                                    </div>
                                )}
                        </li>
                    ))}
                </ul>
                {result.data?.data?.length === 0 && (
                    <p>Noch keine {prediction ? "Vorhersagen" : "Umfragen"}.</p>
                )}
                <div className="flex gap-2">
                    {after && (
                        <Button
                            disabled={result.isFetching}
                            onClick={() => setAfter(undefined)}
                        >
                            Erste Seite
                        </Button>
                    )}
                    {result.data?.pagination?.cursor && (
                        <Button
                            disabled={result.isFetching}
                            onClick={() =>
                                setAfter(result.data?.pagination?.cursor)
                            }
                        >
                            Nächste Seite
                        </Button>
                    )}
                </div>
                <form
                    className="space-y-3"
                    onSubmit={(e) => {
                        e.preventDefault();
                        if (!valid || busy || hasActive) return;
                        action.mutate(
                            prediction
                                ? {
                                      action: "create_prediction",
                                      title,
                                      outcomes: options,
                                      window: duration,
                                  }
                                : {
                                      action: "create_poll",
                                      title,
                                      choices: options,
                                      duration,
                                  },
                            {
                                onSuccess: () => {
                                    setTitle("");
                                    setAfter(undefined);
                                },
                            },
                        );
                    }}
                >
                    <label className="block">
                        {label}-Titel
                        <Input
                            maxLength={prediction ? 45 : 60}
                            value={title}
                            onChange={(e) => setTitle(e.target.value)}
                        />
                    </label>
                    <label className="block">
                        {label}-Antworten
                        <textarea
                            className="w-full bg-panel border border-border rounded p-2"
                            value={answers}
                            onChange={(e) => setAnswers(e.target.value)}
                        />
                    </label>
                    <p className="text-sm text-muted">
                        2–{prediction ? 10 : 5} Antworten, eine pro Zeile,
                        jeweils höchstens 25 Zeichen.
                    </p>
                    <label className="block">
                        {label}-Dauer in Sekunden
                        <Input
                            type="number"
                            min={prediction ? 30 : 15}
                            max={1800}
                            step={1}
                            value={duration}
                            onChange={(e) =>
                                setDuration(Number(e.target.value))
                            }
                        />
                    </label>
                    <Button
                        type="submit"
                        disabled={
                            busy ||
                            !valid ||
                            hasActive ||
                            !result.data ||
                            !!result.error
                        }
                    >
                        {label} starten
                    </Button>
                    {hasActive && <p>Die laufende {label} zuerst beenden.</p>}
                </form>
                {[result.error, action.error, refreshError]
                    .filter(Boolean)
                    .map((error, i) => (
                        <p role="alert" key={i} className="text-danger">
                            {String(error)}
                        </p>
                    ))}
            </section>
        </Card>
    );
}
