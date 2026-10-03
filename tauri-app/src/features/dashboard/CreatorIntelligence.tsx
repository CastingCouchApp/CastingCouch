import { useEffect, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { openPath } from "@tauri-apps/plugin-opener";
import {
    FALLBACK_POLL_MS,
    listenStreamHistory,
    tauriInvoke,
} from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import type {
    ContentRow,
    IntelligenceSnapshot,
} from "./creator-intelligence-types";

const days = [
    "Sonntag",
    "Montag",
    "Dienstag",
    "Mittwoch",
    "Donnerstag",
    "Freitag",
    "Samstag",
];
const number = (n: number, digits = 1) =>
    n.toLocaleString("de-DE", {
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
    });
const signed = (n: number) => (n > 0 ? "+" : "") + number(n);
const date = (at: string) => new Date(at).toLocaleString("de-DE");
function metricName(metric: string) {
    return (
        (
            {
                retention: "Bindung (%)",
                engagement: "Chat/Stunde",
                score: "Creator Score",
                growth: "Follower/Stunde",
                manual: "Manuell",
            } as Record<string, string>
        )[metric] ?? metric
    );
}
function completed(status: string) {
    return status === "Erledigt" || status === "Automatisch erreicht";
}
function Notes({ items }: { items: string[] }) {
    return (
        <ul className="list-disc space-y-1 pl-5 text-sm text-text">
            {items.map((item, index) => (
                <li key={index}>{item}</li>
            ))}
        </ul>
    );
}
function Table({
    heads,
    rows,
    empty = "Noch keine Messdaten vorhanden.",
}: {
    heads: string[];
    rows: ReactNode[][];
    empty?: string;
}) {
    if (!rows.length)
        return <p className="text-sm text-muted-foreground">{empty}</p>;
    return (
        <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
                <thead>
                    <tr>
                        {heads.map((head) => (
                            <th
                                key={head}
                                className="px-2 py-2 font-medium text-muted-foreground"
                            >
                                {head}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {rows.map((row, index) => (
                        <tr key={index} className="border-t border-border">
                            {row.map((cell, column) => (
                                <td
                                    key={column}
                                    className="px-2 py-2 align-top"
                                >
                                    {cell}
                                </td>
                            ))}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
function ContentTable({ rows }: { rows: ContentRow[] }) {
    return (
        <Table
            heads={[
                "Name",
                "Einsätze",
                "Minuten",
                "Ø Zuschauer",
                "Entwicklung",
                "Chat/Minute",
            ]}
            rows={rows.map((r) => [
                r.Name,
                r.Occurrences,
                number(r.TotalMinutes),
                number(r.AverageViewers),
                signed(r.ViewerDelta),
                number(r.ChatMessagesPerMinute),
            ])}
        />
    );
}
function Metric({ label, value }: { label: string; value: ReactNode }) {
    return (
        <div className="rounded border border-border p-3">
            <div className="text-xs text-muted-foreground">{label}</div>
            <div aria-label={label} className="mt-1 text-lg font-medium">
                {value}
            </div>
        </div>
    );
}
export function CreatorIntelligence() {
    const client = useQueryClient();
    const [lookback, setLookback] = useState(30);
    const [listenerError, setListenerError] = useState<string>();
    const [listenerAttempt, setListenerAttempt] = useState(0);
    const [note, setNote] = useState({
        text: "",
        requestId: crypto.randomUUID(),
    });
    const [report, setReport] = useState<string>();
    const query = useQuery({
        queryKey: ["creator-intelligence", lookback],
        queryFn: () =>
            tauriInvoke<IntelligenceSnapshot>("creator_intelligence_snapshot", {
                lookbackDays: lookback,
            }),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const mutation = useMutation({
        mutationFn: (task: () => Promise<unknown>) => task(),
        onSuccess: () => {
            void client.invalidateQueries({
                queryKey: ["creator-intelligence"],
            });
            void client.invalidateQueries({ queryKey: ["stream-history"] });
        },
    });
    useEffect(() => {
        let disposed = false;
        let stop: (() => void) | undefined;
        let timer: ReturnType<typeof setTimeout> | undefined;
        setListenerError(undefined);
        const refresh = () => {
            if (disposed || timer) return;
            timer = setTimeout(() => {
                timer = undefined;
                void client.invalidateQueries({
                    queryKey: ["creator-intelligence"],
                });
            }, 250);
        };
        void listenStreamHistory(refresh)
            .then((fn) => {
                if (disposed) fn();
                else {
                    stop = fn;
                    refresh();
                }
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            stop?.();
            if (timer) clearTimeout(timer);
        };
    }, [client, listenerAttempt]);
    const snapshot = query.data;
    const d = snapshot?.dashboard;
    const refresh = () => {
        mutation.reset();
        setListenerAttempt((a) => a + 1);
        void query.refetch();
    };
    return (
        <Card>
            <section className="space-y-5" aria-label="Creator Intelligence">
                <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                        <h2 className="text-lg font-semibold">
                            Creator Intelligence
                        </h2>
                        <p className="text-sm text-muted-foreground">
                            Analyse aufgezeichneter Streams und beobachteter
                            Entwicklung.
                        </p>
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                        <label className="text-sm">
                            Analysezeitraum{" "}
                            <select
                                aria-label="Analysezeitraum"
                                className="ml-2 rounded border border-border bg-input p-2"
                                value={lookback}
                                onChange={(e) =>
                                    setLookback(Number(e.target.value))
                                }
                            >
                                {[7, 30, 90, 365].map((days) => (
                                    <option key={days} value={days}>
                                        {days} Tage
                                    </option>
                                ))}
                            </select>
                        </label>
                        <Button onClick={refresh} disabled={query.isFetching}>
                            Analyse aktualisieren
                        </Button>
                    </div>
                </div>
                {query.error && (
                    <p role="alert" className="text-sm text-danger">
                        {String(query.error)}
                    </p>
                )}
                {mutation.error && (
                    <p role="alert" className="text-sm text-danger">
                        {String(mutation.error)}
                    </p>
                )}
                {listenerError && (
                    <p role="alert" className="text-sm text-warning">
                        Live-Ereignisse: {listenerError}. Regelmäßige Abfragen
                        bleiben aktiv.
                    </p>
                )}
                {snapshot?.warnings.map((warning, index) => (
                    <p
                        key={index}
                        role="alert"
                        className="text-sm text-warning"
                    >
                        {warning}
                    </p>
                ))}
                {!snapshot && !query.error && (
                    <p className="text-sm text-muted-foreground">
                        Analyse wird geladen …
                    </p>
                )}
                {snapshot && d && (
                    <>
                        <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-6">
                            <Metric
                                label="Vollständige Sessions"
                                value={d.SessionCount}
                            />
                            <Metric
                                label="Creator Score im Zeitraum"
                                value={number(d.AverageCreatorScore)}
                            />
                            <Metric
                                label="Streamqualität"
                                value={d.StreamQualityIndex + "/100"}
                            />
                            <Metric
                                label="Engagement-Index"
                                value={d.EngagementIndex + "/100"}
                            />
                            <Metric
                                label="Wachstumsindex"
                                value={d.GrowthIndex + "/100"}
                            />
                            <Metric
                                label="Ø Zuschauer im Zeitraum"
                                value={number(d.AverageViewers)}
                            />
                        </div>
                        <div className="text-sm text-text">
                            Bindung {number(d.AverageRetentionPercent)} % · Chat{" "}
                            {number(d.AverageChatMessagesPerHour)}/Stunde ·
                            Follower {number(d.AverageFollowersPerHour)}/Stunde
                            <br />
                            Letzte 7 Tage: {d.WeeklySessionCount} Streams ·
                            Score {number(d.WeeklyAverageCreatorScore)}
                            <br />
                            Score-Trend {signed(d.CreatorScoreTrend)} ·
                            Zuschauer-Trend {signed(d.ViewerTrendPerStream)}
                            /Stream
                            <br />
                            Prognose: Ø {number(d.PredictedAverageViewers)}{" "}
                            Zuschauer · Score {d.PredictedCreatorScore}
                            {d.SessionCount > 0 && (
                                <>
                                    <br />
                                    Beste Startzeit: {days[d.BestDay]}{" "}
                                    {String(d.BestStartHour).padStart(2, "0")}
                                    :00 · Kategorie {d.BestCategory}
                                </>
                            )}
                        </div>
                        <Notes items={d.Insights} />
                        {snapshot.latest && (
                            <details open>
                                <summary className="cursor-pointer font-medium">
                                    {snapshot.recording
                                        ? "Laufende Sitzung"
                                        : "Letzte Sitzung"}
                                    : {snapshot.latest.Title || "Ohne Titel"}
                                </summary>
                                <div className="mt-2 space-y-2 text-sm">
                                    <p>
                                        {date(snapshot.latest.StartedAt)} ·{" "}
                                        {snapshot.latest.Duration} ·{" "}
                                        {snapshot.latest.Category ||
                                            "Ohne Kategorie"}
                                    </p>
                                    <p>
                                        Creator Score{" "}
                                        {snapshot.latest.CreatorScore} · Bindung{" "}
                                        {number(
                                            snapshot.latest.RetentionPercent,
                                        )}{" "}
                                        % · Chat{" "}
                                        {number(
                                            snapshot.latest.ChatMessagesPerHour,
                                        )}
                                        /Stunde · Follower{" "}
                                        {number(
                                            snapshot.latest.FollowersPerHour,
                                        )}
                                        /Stunde
                                    </p>
                                    <p>
                                        Ø{" "}
                                        {number(snapshot.latest.AverageViewers)}{" "}
                                        Zuschauer · Peak{" "}
                                        {snapshot.latest.PeakViewers} ·{" "}
                                        {snapshot.latest.ChatMessages}{" "}
                                        Chatnachrichten ·{" "}
                                        {snapshot.latest.Followers} Follower ·{" "}
                                        {snapshot.latest.DistinctScenes} Szenen
                                        · {snapshot.latest.TracksPlayed} Titel
                                    </p>
                                    <Notes
                                        items={snapshot.latest.Recommendations}
                                    />
                                </div>
                            </details>
                        )}
                        <form
                            className="flex flex-wrap items-end gap-2"
                            onSubmit={(e) => {
                                e.preventDefault();
                                mutation.mutate(async () => {
                                    await tauriInvoke("record_creator_note", {
                                        note: note.text,
                                        requestId: note.requestId,
                                    });
                                    setNote((current) =>
                                        current.requestId === note.requestId
                                            ? {
                                                  text: "",
                                                  requestId:
                                                      crypto.randomUUID(),
                                              }
                                            : current,
                                    );
                                });
                            }}
                        >
                            <label className="min-w-48 flex-1 text-sm">
                                Streamnotiz
                                <input
                                    aria-label="Streamnotiz"
                                    value={note.text}
                                    onChange={(e) =>
                                        setNote({
                                            text: e.target.value,
                                            requestId: crypto.randomUUID(),
                                        })
                                    }
                                    className="mt-1 block w-full rounded border border-border bg-input p-2"
                                    placeholder="Was passierte gerade im Stream?"
                                />
                            </label>
                            <Button
                                type="submit"
                                disabled={
                                    !snapshot.recording ||
                                    !note.text.trim() ||
                                    mutation.isPending
                                }
                            >
                                Notiz speichern
                            </Button>
                            {!snapshot.recording && (
                                <p className="w-full text-xs text-muted-foreground">
                                    Notizen benötigen eine aktive
                                    Stream-Sitzung.
                                </p>
                            )}
                        </form>
                        <details>
                            <summary className="cursor-pointer font-medium">
                                Letzte vollständige Sessions
                            </summary>
                            <Table
                                heads={[
                                    "Beginn / Titel",
                                    "Dauer",
                                    "Kategorie",
                                    "Score",
                                    "Ø / Peak",
                                    "Bindung",
                                    "Chat/h",
                                    "Follower/h",
                                ]}
                                rows={d.RecentSessions.map((s) => [
                                    <span>
                                        {date(s.StartedAt)}
                                        <br />
                                        {s.Title}
                                    </span>,
                                    s.Duration,
                                    s.Category,
                                    s.CreatorScore,
                                    number(s.AverageViewers) +
                                        " / " +
                                        s.PeakViewers,
                                    number(s.RetentionPercent) + " %",
                                    number(s.ChatMessagesPerHour),
                                    number(s.FollowersPerHour),
                                ])}
                            />
                        </details>
                        <details open>
                            <summary className="cursor-pointer font-medium">
                                Szenen und Musik
                            </summary>
                            <div className="mt-3 space-y-3">
                                <h3 className="text-sm font-medium">Szenen</h3>
                                <ContentTable rows={snapshot.content.Scenes} />
                                <h3 className="text-sm font-medium">
                                    Musiktitel
                                </h3>
                                <ContentTable rows={snapshot.content.Tracks} />
                                <Notes items={snapshot.content.Insights} />
                            </div>
                        </details>
                        <details>
                            <summary className="cursor-pointer font-medium">
                                Zuschauer nach Wochentag und Uhrzeit
                            </summary>
                            <Table
                                heads={[
                                    "Wochentag",
                                    "Stunde",
                                    "Messpunkte",
                                    "Ø Zuschauer",
                                ]}
                                rows={snapshot.content.Heatmap.map((r) => [
                                    days[r.Day],
                                    String(r.Hour).padStart(2, "0") + ":00",
                                    r.SampleCount,
                                    number(r.AverageViewers),
                                ])}
                            />
                        </details>
                        <details>
                            <summary className="cursor-pointer font-medium">
                                Ereigniskorrelation und Raid-Bindung
                            </summary>
                            <div className="mt-3 space-y-3">
                                <Table
                                    heads={[
                                        "Ereignis",
                                        "Einsätze",
                                        "Vorher",
                                        "Nach 5 Min (Δ)",
                                        "Nach 10 Min (Δ)",
                                    ]}
                                    rows={snapshot.correlation.Correlations.map(
                                        (r) => [
                                            r.EventName,
                                            r.Occurrences,
                                            number(r.BaselineViewers),
                                            signed(r.ViewerDelta5Minutes),
                                            signed(r.ViewerDelta10Minutes),
                                        ],
                                    )}
                                />
                                <h3 className="text-sm font-medium">Raids</h3>
                                <Table
                                    heads={[
                                        "Raid",
                                        "Vorher",
                                        "5 Min",
                                        "10 Min",
                                        "30 Min",
                                        "Bindung 30 Min",
                                    ]}
                                    rows={snapshot.correlation.Raids.map(
                                        (r) => [
                                            r.RaidSummary,
                                            number(r.ViewersBefore),
                                            number(r.ViewersAfter5),
                                            number(r.ViewersAfter10),
                                            number(r.ViewersAfter30),
                                            number(r.Retention30Percent) + " %",
                                        ],
                                    )}
                                />
                                <Notes items={snapshot.correlation.Actions} />
                            </div>
                        </details>
                        <details open>
                            <summary className="cursor-pointer font-medium">
                                Maßnahmen · letzte 30 Tage
                            </summary>
                            {snapshot.actions ? (
                                <>
                                    <p className="my-2 text-sm text-muted-foreground">
                                        {snapshot.actions.OpenCount} offen ·{" "}
                                        {snapshot.actions.CompletedCount}{" "}
                                        abgeschlossen
                                    </p>
                                    <ul className="space-y-3">
                                        {snapshot.actions.Items.slice(
                                            0,
                                            20,
                                        ).map((item) => (
                                            <li
                                                key={item.Id}
                                                className="rounded border border-border p-3 text-sm"
                                            >
                                                <div className="font-medium">
                                                    {item.Title}
                                                </div>
                                                <p className="my-1 text-xs text-muted-foreground">
                                                    Priorität {item.Priority} ·{" "}
                                                    {item.Status} ·{" "}
                                                    {metricName(item.Metric)} ·
                                                    Basis{" "}
                                                    {number(item.Baseline)} ·
                                                    Aktuell{" "}
                                                    {number(
                                                        item.CurrentValue ??
                                                            item.Baseline,
                                                    )}{" "}
                                                    · Ziel {number(item.Target)}
                                                </p>
                                                <div className="flex flex-wrap gap-2">
                                                    {!completed(
                                                        item.Status,
                                                    ) && (
                                                        <Button
                                                            disabled={
                                                                mutation.isPending
                                                            }
                                                            onClick={() =>
                                                                mutation.mutate(
                                                                    () =>
                                                                        tauriInvoke(
                                                                            "complete_creator_action",
                                                                            {
                                                                                actionId:
                                                                                    item.Id,
                                                                            },
                                                                        ),
                                                                )
                                                            }
                                                        >
                                                            Als erledigt
                                                            markieren
                                                        </Button>
                                                    )}
                                                    {item.Metric !==
                                                        "manual" && (
                                                        <Button
                                                            disabled={
                                                                mutation.isPending ||
                                                                snapshot.experiments ===
                                                                    null ||
                                                                snapshot.experiments.Rows.some(
                                                                    (r) =>
                                                                        r.ActionId ===
                                                                            item.Id &&
                                                                        r.Status ===
                                                                            "Aktiv",
                                                                )
                                                            }
                                                            onClick={() =>
                                                                mutation.mutate(
                                                                    () =>
                                                                        tauriInvoke(
                                                                            "start_creator_experiment",
                                                                            {
                                                                                actionId:
                                                                                    item.Id,
                                                                            },
                                                                        ),
                                                                )
                                                            }
                                                        >
                                                            Experiment starten
                                                        </Button>
                                                    )}
                                                </div>
                                            </li>
                                        ))}
                                    </ul>
                                </>
                            ) : (
                                <p className="text-sm text-warning">
                                    Maßnahmen konnten nicht geladen oder
                                    gespeichert werden.
                                </p>
                            )}
                        </details>
                        <details>
                            <summary className="cursor-pointer font-medium">
                                Wirkung der Maßnahmen
                            </summary>
                            {snapshot.effectiveness && (
                                <div className="mt-2 space-y-2">
                                    <p className="text-sm">
                                        {snapshot.effectiveness.Summary}
                                    </p>
                                    <p className="text-xs text-muted-foreground">
                                        {snapshot.effectiveness.ImprovedCount}{" "}
                                        verbessert ·{" "}
                                        {snapshot.effectiveness.DeclinedCount}{" "}
                                        verschlechtert ·{" "}
                                        {snapshot.effectiveness.ReachedCount}{" "}
                                        abgeschlossen
                                    </p>
                                    <Table
                                        heads={[
                                            "Maßnahme",
                                            "Status",
                                            "Basis → Aktuell → Ziel",
                                            "Fortschritt",
                                            "Bewertung",
                                        ]}
                                        rows={snapshot.effectiveness.Rows.slice(
                                            0,
                                            15,
                                        ).map((r) => [
                                            r.Title,
                                            r.Status,
                                            number(r.Baseline) +
                                                " → " +
                                                number(r.Current) +
                                                " → " +
                                                number(r.Target),
                                            signed(r.Improvement) +
                                                " / " +
                                                number(r.ProgressPercent) +
                                                " %",
                                            r.Verdict,
                                        ])}
                                    />
                                </div>
                            )}
                        </details>
                        <details open>
                            <summary className="cursor-pointer font-medium">
                                Experimente
                            </summary>
                            {snapshot.experiments && (
                                <div className="mt-2 space-y-2">
                                    <p className="text-sm">
                                        {snapshot.experiments.Summary}
                                    </p>
                                    <Table
                                        heads={[
                                            "Experiment / Start",
                                            "Status",
                                            "Sessions",
                                            "Basis → Aktuell",
                                            "Veränderung",
                                            "Bewertung",
                                        ]}
                                        rows={snapshot.experiments.Rows.slice(
                                            0,
                                            15,
                                        ).map((r) => [
                                            <span>
                                                {r.Title}
                                                <br />
                                                {date(r.StartedAt)}
                                            </span>,
                                            r.Status,
                                            r.SessionCount +
                                                " / " +
                                                r.TargetSessions,
                                            number(r.Baseline) +
                                                " → " +
                                                number(r.Current),
                                            signed(r.Delta),
                                            r.Confidence + " · " + r.Verdict,
                                        ])}
                                    />
                                </div>
                            )}
                        </details>
                    </>
                )}
                <div className="flex flex-wrap gap-2">
                    <Button
                        disabled={mutation.isPending}
                        onClick={() =>
                            mutation.mutate(async () => {
                                setReport(
                                    await tauriInvoke<string>(
                                        "generate_creator_weekly_report",
                                    ),
                                );
                            })
                        }
                    >
                        Wochenbericht erstellen
                    </Button>
                    {report && (
                        <Button
                            disabled={mutation.isPending}
                            onClick={() =>
                                mutation.mutate(() => openPath(report))
                            }
                        >
                            Wochenbericht öffnen
                        </Button>
                    )}
                    <Button
                        disabled={mutation.isPending}
                        onClick={() =>
                            mutation.mutate(() =>
                                tauriInvoke("open_creator_intelligence_folder"),
                            )
                        }
                    >
                        Analysedaten öffnen
                    </Button>
                </div>
            </section>
        </Card>
    );
}
