import { useEffect, useRef, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import {
    FALLBACK_POLL_MS,
    listenDashboardChanged,
    queryKeys,
    tauriInvoke,
} from "../../lib/api";
import type {
    DashboardDraft,
    DashboardPreferences,
} from "../../lib/command-contract";
import {
    CARD_GROUPS,
    CARD_TITLES,
    cardVisible,
    dashboardKey,
    type DashboardSnapshot,
} from "./dashboard-types";
import { DashboardSceneEditor } from "./DashboardSceneEditor";
const selectClass =
    "rounded-md border border-border bg-input px-2 py-1 text-text";
function errorText(error: unknown) {
    return error instanceof Error ? error.message : String(error);
}
const groups: [keyof DashboardPreferences, string][] = [
    ["showServiceStatus", "Verbindungsstatus"],
    ["showStreamControls", "Streamsteuerung"],
    ["showLivePanels", "Live-Panels"],
    ["showAdvancedTools", "Auswertungen"],
    ["showNotifications", "Ereignisse"],
    ["showStreamHistory", "Verlauf"],
];
export function DashboardCards({
    draft,
    focus,
    nodes,
}: {
    draft: DashboardDraft;
    focus: boolean;
    nodes: Record<string, ReactNode>;
}) {
    const visible = draft.cards.filter(
        (c) => cardVisible(draft, c.key, focus) && nodes[c.key],
    );
    const zones = ["Left", "Center", "Right"].filter((zone) =>
        visible.some((c) => c.zone === zone),
    );
    return (
        <div
            className={`flex flex-col items-stretch gap-4 xl:grid xl:items-start ${zones.length === 3 ? "xl:grid-cols-3" : zones.length === 2 ? "xl:grid-cols-2" : ""}`}
        >
            {zones.map((zone) => (
                <div
                    key={zone}
                    data-dashboard-zone={zone}
                    className="contents min-w-0 xl:block xl:space-y-4"
                >
                    {visible
                        .filter((c) => c.zone === zone)
                        .map((c) => (
                            <section
                                key={c.key}
                                data-dashboard-card={c.key}
                                data-dashboard-size={c.size}
                                style={{ order: draft.cards.indexOf(c) }}
                                className={
                                    c.size === "Kompakt"
                                        ? "max-h-96 overflow-auto text-sm"
                                        : c.size === "Standard"
                                          ? "max-h-[44rem] overflow-auto"
                                          : ""
                                }
                            >
                                {nodes[c.key]}
                            </section>
                        ))}
                </div>
            ))}
        </div>
    );
}
function preset(draft: DashboardDraft, name: string): DashboardDraft {
    const next = structuredClone(draft);
    for (const [key] of groups)
        (next.preferences as unknown as Record<string, unknown>)[key] = true;
    next.preferences.showQuickServices = true;
    next.cards.forEach((c) => {
        c.visible = true;
        c.size = name === "Kompakt" ? "Kompakt" : "Standard";
    });
    if (name === "Minimal") {
        next.preferences.showLivePanels = false;
        next.preferences.showQuickServices = false;
        next.preferences.showNotifications = false;
        next.preferences.showStreamHistory = false;
        next.cards
            .filter((c) => ["StreamControl", "ObsSceneControl"].includes(c.key))
            .forEach((c) => (c.size = "Groß"));
    }
    if (name === "Twitch Fokus" || name === "OBS Fokus") {
        const priority =
            name === "Twitch Fokus"
                ? ["Community", "TwitchChat", "TwitchEvents", "SpotifyPlayer"]
                : ["ObsSceneControl", "StreamControl", "StreamEnd", "Countdown"];
        next.cards.sort(
            (a, b) =>
                (priority.includes(a.key)
                    ? priority.indexOf(a.key)
                    : priority.length) -
                (priority.includes(b.key)
                    ? priority.indexOf(b.key)
                    : priority.length),
        );
        // Keep priority cards first; the remaining order is stable.
        next.cards = [
            ...next.cards.filter((c) => priority.includes(c.key)),
            ...next.cards.filter((c) => !priority.includes(c.key)),
        ];
        next.cards
            .filter((c) => priority.includes(c.key))
            .forEach((c) => (c.size = "Groß"));
        if (name === "OBS Fokus") next.preferences.showStreamHistory = false;
        if (name === "Twitch Fokus") next.preferences.showQuickServices = false;
    }
    return next;
}
export function DashboardLayout({
    live,
    children,
}: {
    live?: boolean;
    children: (
        draft: DashboardDraft,
        focus: boolean,
        original: unknown,
    ) => ReactNode;
}) {
    const client = useQueryClient();
    const layout = useQuery({
        queryKey: dashboardKey,
        queryFn: () => tauriInvoke<DashboardSnapshot>("dashboard_snapshot"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const [editor, setEditor] = useState<DashboardSnapshot>();
    const [focus, setFocus] = useState(false);
    const lastLive = useRef<boolean | undefined>(undefined);
    const [listenerError, setListenerError] = useState<string>();
    const [attempt, setAttempt] = useState(0);
    useEffect(() => {
        let disposed = false,
            unlisten: (() => void) | undefined;
        setListenerError(undefined);
        const refresh = async () => {
            await client.cancelQueries({ queryKey: dashboardKey });
            if (!disposed)
                await client.invalidateQueries({ queryKey: dashboardKey });
        };
        void listenDashboardChanged(() => void refresh())
            .then((fn) => {
                if (disposed) fn();
                else {
                    unlisten = fn;
                    void refresh();
                }
            })
            .catch((e) => {
                if (!disposed) setListenerError(errorText(e));
            });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [client, attempt]);
    useEffect(() => {
        if (live === undefined || !layout.data) return;
        const previous = lastLive.current;
        lastLive.current = live;
        if (
            live &&
            !previous &&
            layout.data.draft.preferences.autoFocusModeOnStreamStart
        )
            setFocus(true);
        if (
            !live &&
            previous &&
            layout.data.draft.preferences.autoExitFocusModeOnStreamEnd
        )
            setFocus(false);
    }, [live, layout.data]);
    const save = useMutation({
        mutationFn: (value: DashboardSnapshot) =>
            tauriInvoke<DashboardSnapshot>("save_dashboard", {
                original: value.original,
                draft: value.draft,
            }),
        onSuccess: (result) => {
            client.setQueryData(dashboardKey, result);
            void client.invalidateQueries({ queryKey: queryKeys.settings });
            setEditor(undefined);
        },
    });
    const update = (edit: (draft: DashboardDraft) => DashboardDraft) =>
        setEditor((current) =>
            current ? { ...current, draft: edit(current.draft) } : current,
        );
    if (!layout.data)
        return (
            <Card>
                {layout.isError ? (
                    <>
                        <p role="alert" className="text-danger">
                            {errorText(layout.error)}
                        </p>
                        <Button onClick={() => void layout.refetch()}>
                            Dashboard erneut laden
                        </Button>
                    </>
                ) : (
                    <p>Dashboard wird geladen …</p>
                )}
            </Card>
        );
    const data = layout.data;
    return (
        <div className="space-y-4">
            <div className="flex flex-wrap gap-2">
                <Button
                    variant="ghost"
                    disabled={save.isPending || Boolean(editor)}
                    onClick={() => {
                        save.reset();
                        setEditor(structuredClone(data));
                    }}
                >
                    Dashboard konfigurieren
                </Button>
                <Button
                    variant="ghost"
                    aria-pressed={focus}
                    onClick={() => setFocus((value) => !value)}
                >
                    {focus ? "Fokusmodus beenden" : "Fokusmodus"}
                </Button>
            </div>
            {layout.isError && (
                <p role="alert" className="text-danger">
                    {errorText(layout.error)} · Letztes Layout wird angezeigt.
                </p>
            )}
            {listenerError && (
                <p role="alert" className="text-danger">
                    Dashboard-Ereignisse: {listenerError}{" "}
                    <Button
                        variant="ghost"
                        onClick={() => setAttempt((a) => a + 1)}
                    >
                        Ereignisse erneut verbinden
                    </Button>
                </p>
            )}
            {data.warnings.map((warning, i) => (
                <p key={i} role="status" className="text-muted">
                    {warning}
                </p>
            ))}
            {editor && (
                <Card
                    role="dialog"
                    aria-label="Dashboard konfigurieren"
                    className="space-y-4"
                >
                    <h2 className="text-lg font-semibold">
                        Dashboard konfigurieren
                    </h2>
                    <fieldset disabled={save.isPending} className="space-y-4">
                        <div className="flex flex-wrap gap-2">
                            {[
                                "Standard",
                                "Kompakt",
                                "Twitch Fokus",
                                "OBS Fokus",
                                "Minimal",
                            ].map((name) => (
                                <Button
                                    key={name}
                                    variant="ghost"
                                    onClick={() =>
                                        update((d) => preset(d, name))
                                    }
                                >
                                    {name}
                                </Button>
                            ))}
                        </div>
                        <div className="flex flex-wrap gap-4">
                            {groups.map(([key, label]) => (
                                <label
                                    key={key}
                                    className="flex items-center gap-2"
                                >
                                    <input
                                        type="checkbox"
                                        checked={
                                            editor.draft.preferences[key] ===
                                            true
                                        }
                                        onChange={(e) =>
                                            update((d) => ({
                                                ...d,
                                                preferences: {
                                                    ...d.preferences,
                                                    [key]: e.target.checked,
                                                },
                                            }))
                                        }
                                    />
                                    {label}
                                </label>
                            ))}
                        </div>
                        <div className="space-y-2">
                            {editor.draft.cards.map((c, i) => {
                                const name = CARD_TITLES[c.key] ?? c.key;
                                return (
                                    <div
                                        key={c.key}
                                        className="flex flex-wrap items-center gap-3 rounded border border-border p-2"
                                    >
                                        <label className="flex items-center gap-2">
                                            <input
                                                aria-label={`${name} anzeigen`}
                                                type="checkbox"
                                                checked={c.visible}
                                                onChange={(e) =>
                                                    update((d) => ({
                                                        ...d,
                                                        cards: d.cards.map(
                                                            (card) =>
                                                                card.key ===
                                                                c.key
                                                                    ? {
                                                                          ...card,
                                                                          visible:
                                                                              e
                                                                                  .target
                                                                                  .checked,
                                                                      }
                                                                    : card,
                                                        ),
                                                    }))
                                                }
                                            />
                                            {name}
                                        </label>
                                        {editor.draft.preferences[
                                            CARD_GROUPS[c.key]
                                        ] === false && (
                                            <span className="text-sm text-muted">
                                                Gruppe ausgeblendet
                                            </span>
                                        )}
                                        <label>
                                            Spalte{" "}
                                            <select
                                                className={selectClass}
                                                aria-label={`${name} Spalte`}
                                                value={c.zone}
                                                onChange={(e) =>
                                                    update((d) => ({
                                                        ...d,
                                                        cards: d.cards.map(
                                                            (card) =>
                                                                card.key ===
                                                                c.key
                                                                    ? {
                                                                          ...card,
                                                                          zone: e
                                                                              .target
                                                                              .value,
                                                                      }
                                                                    : card,
                                                        ),
                                                    }))
                                                }
                                            >
                                                {[
                                                    ["Left", "Links"],
                                                    ["Center", "Mitte"],
                                                    ["Right", "Rechts"],
                                                ].map(([value, label]) => (
                                                    <option
                                                        key={value}
                                                        value={value}
                                                    >
                                                        {label}
                                                    </option>
                                                ))}
                                            </select>
                                        </label>
                                        <label>
                                            Größe{" "}
                                            <select
                                                className={selectClass}
                                                aria-label={`${name} Größe`}
                                                value={c.size}
                                                onChange={(e) =>
                                                    update((d) => ({
                                                        ...d,
                                                        cards: d.cards.map(
                                                            (card) =>
                                                                card.key ===
                                                                c.key
                                                                    ? {
                                                                          ...card,
                                                                          size: e
                                                                              .target
                                                                              .value,
                                                                      }
                                                                    : card,
                                                        ),
                                                    }))
                                                }
                                            >
                                                {[
                                                    "Kompakt",
                                                    "Standard",
                                                    "Groß",
                                                ].map((size) => (
                                                    <option key={size}>
                                                        {size}
                                                    </option>
                                                ))}
                                            </select>
                                        </label>
                                        {[-1, 1].map((delta) => (
                                            <Button
                                                key={delta}
                                                variant="ghost"
                                                aria-label={`${name} nach ${delta === -1 ? "oben" : "unten"}`}
                                                disabled={
                                                    i + delta < 0 ||
                                                    i + delta >=
                                                        editor.draft.cards
                                                            .length
                                                }
                                                onClick={() =>
                                                    update((d) => {
                                                        const cards = [
                                                            ...d.cards,
                                                        ];
                                                        [
                                                            cards[i],
                                                            cards[i + delta],
                                                        ] = [
                                                            cards[i + delta],
                                                            cards[i],
                                                        ];
                                                        return { ...d, cards };
                                                    })
                                                }
                                            >
                                                {delta === -1 ? "↑" : "↓"}
                                            </Button>
                                        ))}
                                    </div>
                                );
                            })}
                        </div>
                        <div className="flex flex-wrap gap-3">
                            <label className="flex items-center gap-2">
                                <input
                                    type="checkbox"
                                    checked={
                                        editor.draft.preferences
                                            .autoFocusModeOnStreamStart
                                    }
                                    onChange={(e) =>
                                        update((d) => ({
                                            ...d,
                                            preferences: {
                                                ...d.preferences,
                                                autoFocusModeOnStreamStart:
                                                    e.target.checked,
                                            },
                                        }))
                                    }
                                />
                                Fokusmodus bei Streamstart
                            </label>
                            <label className="flex items-center gap-2">
                                <input
                                    type="checkbox"
                                    checked={
                                        editor.draft.preferences
                                            .autoExitFocusModeOnStreamEnd
                                    }
                                    onChange={(e) =>
                                        update((d) => ({
                                            ...d,
                                            preferences: {
                                                ...d.preferences,
                                                autoExitFocusModeOnStreamEnd:
                                                    e.target.checked,
                                            },
                                        }))
                                    }
                                />
                                Fokusmodus bei Streamende verlassen
                            </label>
                        </div>
                        <label>
                            OBS-Vorschaugröße{" "}
                            <select
                                className={selectClass}
                                value={
                                    editor.draft.preferences.obsScenePreviewSize
                                }
                                onChange={(e) =>
                                    update((d) => ({
                                        ...d,
                                        preferences: {
                                            ...d.preferences,
                                            obsScenePreviewSize: e.target.value,
                                        },
                                    }))
                                }
                            >
                                {["Kompakt", "Standard", "Groß"].map((s) => (
                                    <option key={s}>{s}</option>
                                ))}
                            </select>
                        </label>
                        <label className="block">
                            Hauptkennzahl{" "}
                            <select
                                className={selectClass}
                                value={
                                    editor.draft.preferences.dashboardStatistic
                                }
                                onChange={(e) =>
                                    update((d) => ({
                                        ...d,
                                        preferences: {
                                            ...d.preferences,
                                            dashboardStatistic: e.target.value,
                                        },
                                    }))
                                }
                            >
                                {[
                                    ["ViewerCount", "Zuschauer"],
                                    ["FollowerCount", "Follower"],
                                    ["SubscriberCount", "Subscriptions"],
                                    ["NewFollowers", "Neue Follower"],
                                    ["NewSubscribers", "Neue Subscriptions"],
                                    ["ChatterCount", "Chatter"],
                                ].map(([value, label]) => (
                                    <option key={value} value={value}>
                                        {label}
                                    </option>
                                ))}
                            </select>
                        </label>
                        <DashboardSceneEditor
                            buttons={editor.draft.sceneButtons}
                            sceneChoices={editor.sceneChoices}
                            onChange={(edit) =>
                                update((d) => ({
                                    ...d,
                                    sceneButtons: edit(d.sceneButtons),
                                }))
                            }
                        />
                        <div className="flex gap-2">
                            <Button onClick={() => save.mutate(editor)}>
                                Dashboard speichern
                            </Button>
                            <Button
                                variant="ghost"
                                onClick={() => {
                                    save.reset();
                                    setEditor(undefined);
                                }}
                            >
                                Abbrechen
                            </Button>
                        </div>
                    </fieldset>
                    {save.isError && (
                        <p role="alert" className="text-danger">
                            {errorText(save.error)}
                        </p>
                    )}
                </Card>
            )}
            {children(data.draft, focus, data.original)}
        </div>
    );
}
