import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    listenStreamEnd,
    listenStreamEndSettings,
    tauriInvoke,
} from "../../lib/api";
import type {
    StreamEndPreferences,
    StreamEndSnapshot,
} from "../../lib/command-contract";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";

type Configuration = {
    original: unknown;
    draft: StreamEndPreferences;
    endScene: string;
    raidChannels: string[];
    outgoingRaid: {
        available: boolean;
        broadcasterId: string;
        error: string | null;
    };
    warnings: string[];
};
export const streamEndStatusKey = ["stream-end-status"] as const;
const configurationKey = ["stream-end-settings"] as const;
const actionNames: Record<string, string> = {
    abort: "Abbrechen",
    cancel_raid: "Raid abbrechen",
    skip_raid: "Ohne Raid beenden",
    raid_now: "Jetzt raiden",
    start_now: "Geplantes Streamende starten",
    skip_end: "Endszene überspringen",
};

export function StreamEndPanel({
    enabled,
    live,
    defaultExpanded = false,
    onClose,
}: {
    enabled: boolean;
    live?: boolean;
    defaultExpanded?: boolean;
    onClose?: () => void;
}) {
    const client = useQueryClient();
    const [expanded, setExpanded] = useState(defaultExpanded);
    const [editor, setEditor] = useState<Configuration>();
    const [dirty, setDirty] = useState(false);
    const [closing, setClosing] = useState(false);
    const status = useQuery({
        queryKey: streamEndStatusKey,
        queryFn: () => tauriInvoke<StreamEndSnapshot>("stream_end_status"),
        refetchInterval: 1000,
    });
    const configuration = useQuery({
        queryKey: configurationKey,
        queryFn: () => tauriInvoke<Configuration>("stream_end_snapshot"),
        enabled: expanded || status.data?.active === true,
        refetchInterval: 3000,
    });
    useEffect(() => {
        if (configuration.data?.draft && !dirty) setEditor(configuration.data);
    }, [configuration.data, dirty]);
    useEffect(() => {
        let disposed = false;
        const stops: Array<() => void> = [];
        const register = (promise: Promise<() => void>) =>
            void promise
                .then((stop) => (disposed ? stop() : stops.push(stop)))
                .catch(() => {});
        register(
            listenStreamEnd((value) =>
                client.setQueryData(streamEndStatusKey, value),
            ),
        );
        register(
            listenStreamEndSettings(
                () =>
                    void client.invalidateQueries({
                        queryKey: configurationKey,
                    }),
            ),
        );
        return () => {
            disposed = true;
            stops.forEach((stop) => stop());
        };
    }, [client]);
    useEffect(() => {
        if (closing && status.data && !status.data.active) {
            setClosing(false);
            onClose?.();
        }
    }, [closing, status.data, onClose]);

    const save = useMutation({
        mutationFn: async (action: "save" | "start" | "plan") => {
            if (!editor) throw Error("Streamende-Einstellungen fehlen");
            const saved = await tauriInvoke<Configuration>(
                "save_stream_end_preferences",
                { original: editor.original, draft: editor.draft },
            );
            setEditor(saved);
            setDirty(false);
            client.setQueryData(configurationKey, saved);
            if (action === "save") return;
            const next = await tauriInvoke<StreamEndSnapshot>(
                "start_stream_end",
                {
                    planned: action === "plan",
                    ...(action === "plan"
                        ? { seconds: saved.draft.plannedSeconds }
                        : {}),
                    reviewed: saved.original,
                },
            );
            client.setQueryData(streamEndStatusKey, next);
            void client.invalidateQueries({ queryKey: ["obs-outputs"] });
        },
    });
    const control = useMutation({
        mutationFn: async (action: string) => {
            const next = await tauriInvoke<StreamEndSnapshot>(
                "stream_end_control",
                { action },
            );
            client.setQueryData(streamEndStatusKey, next);
        },
        onError: () => setClosing(false),
    });
    const resolve = useMutation({
        mutationFn: async (
            action: "cancel_twitch_raid" | "acknowledge_twitch_raid",
        ) => {
            await tauriInvoke(action);
            await client.invalidateQueries({ queryKey: streamEndStatusKey });
        },
    });
    const draft = editor?.draft;
    const runtime = status.data;
    const active = runtime?.active === true;
    const pending = control.isPending || !!runtime?.pendingAction;
    const abortAvailable =
        active && !["stopping", "finalizing"].includes(runtime?.phase ?? "");
    const showEditor = expanded || active || !!onClose;
    const change = <K extends keyof StreamEndPreferences>(
        key: K,
        value: StreamEndPreferences[K],
    ) => {
        if (!editor) return;
        setDirty(true);
        setEditor({ ...editor, draft: { ...editor.draft, [key]: value } });
    };
    const valid =
        !!draft &&
        ["Immediate", "EndSceneThenStop", "EndSceneRaidThenStop"].includes(
            draft.mode,
        ) &&
        [
            draft.endSceneSeconds,
            draft.plannedSeconds,
            draft.plannedMinutes,
            draft.raidCountdownSeconds,
            draft.raidStartTimeoutSeconds,
        ].every(Number.isSafeInteger) &&
        draft.endSceneSeconds >= 0 &&
        draft.endSceneSeconds <= 2147483647 &&
        draft.plannedSeconds >= 0 &&
        draft.plannedSeconds <= 2147483647 &&
        draft.raidCountdownSeconds >= 5 &&
        draft.raidCountdownSeconds <= 300 &&
        draft.raidStartTimeoutSeconds >= 15 &&
        draft.raidStartTimeoutSeconds <= 600 &&
        (!draft.selectedRaidChannel.trim() ||
            /^@?[A-Za-z0-9_]+$/.test(draft.selectedRaidChannel.trim()));
    const raidConfigured = !!draft?.selectedRaidChannel.trim();
    const raidReady = configuration.data?.outgoingRaid?.available === true;
    const manualRaid = draft?.mode === "EndSceneRaidThenStop" && raidConfigured;
    const plannedRaid = draft?.raidOnStreamEnd === true && raidConfigured;
    const canStart =
        enabled &&
        live === true &&
        valid &&
        !active &&
        !save.isPending &&
        !runtime?.raidPending &&
        !status.isError;
    const number = (
        label: string,
        key:
            | "endSceneSeconds"
            | "raidCountdownSeconds"
            | "raidStartTimeoutSeconds"
            | "plannedSeconds",
        min: number,
        max: number,
    ) => (
        <label className="block space-y-1">
            {label}
            <Input
                type="number"
                min={min}
                max={max}
                value={draft?.[key] ?? 0}
                disabled={active || save.isPending}
                onChange={(event) => change(key, Number(event.target.value))}
            />
        </label>
    );
    const checkbox = (
        label: string,
        key: "stopStreamAfterRaid" | "stopMusicAfterRaid" | "raidOnStreamEnd",
    ) => (
        <label className="flex items-center gap-2">
            <input
                type="checkbox"
                checked={draft?.[key] ?? false}
                disabled={active || save.isPending}
                onChange={(event) => change(key, event.target.checked)}
            />
            {label}
        </label>
    );
    const action = (key: string, label: string, available = true) => (
        <Button
            key={key}
            disabled={!available || pending || status.isError}
            onClick={() => control.mutate(key)}
        >
            {label}
        </Button>
    );
    return (
        <Card className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="text-lg font-semibold">Streamende und Raid</h2>
                {!onClose && !active && (
                    <Button onClick={() => setExpanded(!expanded)}>
                        {expanded ? "Assistent verbergen" : "Assistent öffnen"}
                    </Button>
                )}
            </div>
            <p aria-live="polite">
                {status.isError
                    ? "Streamende-Status unbekannt"
                    : (runtime?.status ?? "Streamende-Status wird geladen …")}
            </p>
            {active && (
                <div className="space-y-2">
                    <p className="text-3xl tabular-nums">
                        {runtime?.remainingSeconds ?? 0} s
                    </p>
                    {runtime?.targetLogin && (
                        <p>
                            Raid-Ziel:{" "}
                            {runtime.targetDisplayName || runtime.targetLogin}
                            {runtime.attempt > 0
                                ? ` · Versuch ${runtime.attempt}`
                                : ""}
                        </p>
                    )}
                    {["awaiting_raid", "raid_uncertain"].includes(
                        runtime?.phase ?? "",
                    ) && (
                        <p>
                            Die Bestätigung von Twitch wird abgewartet. Der
                            lokale Countdown beendet den Stream noch nicht.
                        </p>
                    )}
                    {runtime?.pendingAction && (
                        <p>
                            Aktion wird ausgeführt:{" "}
                            {actionNames[runtime.pendingAction] ??
                                runtime.pendingAction}
                        </p>
                    )}
                    <div className="flex flex-wrap gap-2">
                        {runtime?.phase === "scheduled" &&
                            action(
                                "start_now",
                                "Geplantes Streamende jetzt starten",
                            )}
                        {runtime?.phase === "end_scene" &&
                            action("skip_end", "Endszene überspringen")}
                        {[
                            "raid_probe",
                            "raid_retry",
                            "raid_starting",
                            "raid_countdown",
                            "awaiting_raid",
                            "raid_uncertain",
                        ].includes(runtime?.phase ?? "") &&
                            action("skip_raid", "Ohne Raid beenden")}
                        {runtime?.raidPending &&
                            action("cancel_raid", "Raid abbrechen")}
                        {runtime?.raidPending &&
                            action(
                                "raid_now",
                                "Jetzt raiden",
                                runtime.canRaidNow,
                            )}
                        {!onClose &&
                            action(
                                "abort",
                                "Streamende abbrechen",
                                abortAvailable,
                            )}
                    </div>
                </div>
            )}
            {!active && runtime?.raidPending && (
                <div className="space-y-2">
                    <p>
                        Raid für{" "}
                        {runtime.broadcasterLogin || "den bisherigen Kanal"} zu{" "}
                        {runtime.targetLogin} ist noch unaufgelöst. Vor einem
                        neuen Ablauf in Twitch prüfen oder abbrechen.
                    </p>
                    <div className="flex flex-wrap gap-2">
                        <Button
                            disabled={resolve.isPending}
                            onClick={() => resolve.mutate("cancel_twitch_raid")}
                        >
                            Unaufgelösten Raid abbrechen
                        </Button>
                        <Button
                            disabled={resolve.isPending}
                            onClick={() =>
                                resolve.mutate("acknowledge_twitch_raid")
                            }
                        >
                            In Twitch geprüft: Raid erledigt
                        </Button>
                    </div>
                </div>
            )}
            {showEditor && (
                <>
                    {configuration.isError && (
                        <p role="alert">{String(configuration.error)}</p>
                    )}
                    {!draft && !configuration.isError && (
                        <p>Einstellungen werden geladen …</p>
                    )}
                    {draft && (
                        <fieldset
                            disabled={active || save.isPending}
                            className="space-y-3"
                        >
                            <label className="block space-y-1">
                                Streamende-Modus
                                <select
                                    className="w-full rounded border border-border bg-panel px-3 py-2"
                                    value={draft.mode}
                                    onChange={(event) =>
                                        change("mode", event.target.value)
                                    }
                                >
                                    <option value="Immediate">
                                        Sofort beenden
                                    </option>
                                    <option value="EndSceneThenStop">
                                        Endszene, dann beenden
                                    </option>
                                    <option value="EndSceneRaidThenStop">
                                        Endszene mit Raid
                                    </option>
                                </select>
                            </label>
                            <p>
                                Endszene:{" "}
                                {configuration.data?.endScene ||
                                    "Keine Szene konfiguriert"}
                            </p>
                            {number(
                                "Endszene in Sekunden",
                                "endSceneSeconds",
                                0,
                                2147483647,
                            )}
                            <label className="block space-y-1">
                                Raid-Ziel
                                <Input
                                    list="stream-end-raid-channels"
                                    value={draft.selectedRaidChannel}
                                    onChange={(event) =>
                                        change(
                                            "selectedRaidChannel",
                                            event.target.value,
                                        )
                                    }
                                />
                            </label>
                            <datalist id="stream-end-raid-channels">
                                {(configuration.data?.raidChannels ?? [])
                                    .filter(
                                        (value) => typeof value === "string",
                                    )
                                    .map((value) => (
                                        <option key={value} value={value} />
                                    ))}
                            </datalist>
                            {number(
                                "Raid-Countdown in Sekunden",
                                "raidCountdownSeconds",
                                5,
                                300,
                            )}
                            {number(
                                "Raid-Start-Timeout in Sekunden",
                                "raidStartTimeoutSeconds",
                                15,
                                600,
                            )}
                            {checkbox(
                                "Stream nach bestätigtem Raid stoppen",
                                "stopStreamAfterRaid",
                            )}
                            {checkbox(
                                "Musik nach bestätigtem Raid pausieren",
                                "stopMusicAfterRaid",
                            )}
                            {checkbox(
                                "Bei geplantem Streamende raiden",
                                "raidOnStreamEnd",
                            )}
                            {number(
                                "Geplantes Streamende in Sekunden",
                                "plannedSeconds",
                                1,
                                2147483647,
                            )}
                            <p>
                                Geplant:{" "}
                                {plannedRaid
                                    ? "Endszene mit Raid"
                                    : "Endszene, dann beenden"}
                                . Die normale Modusauswahl bleibt davon
                                unabhängig.
                            </p>
                            {!raidReady && (manualRaid || plannedRaid) && (
                                <p role="alert">
                                    Ausgehende Raid-Bestätigung nicht verfügbar:{" "}
                                    {configuration.data?.outgoingRaid?.error ??
                                        "Twitch EventSub verbinden"}
                                </p>
                            )}
                            {!raidConfigured &&
                                (draft.mode === "EndSceneRaidThenStop" ||
                                    draft.raidOnStreamEnd) && (
                                    <p>
                                        Raid-Ziel fehlt; der Ablauf verwendet
                                        nur die Endszene.
                                    </p>
                                )}
                            {!enabled ? (
                                <p>OBS nicht verbunden.</p>
                            ) : live === undefined ? (
                                <p>OBS-Streamstatus unbekannt.</p>
                            ) : !live ? (
                                <p>OBS-Stream läuft nicht.</p>
                            ) : null}
                            {!valid && (
                                <p role="alert">
                                    Bitte gültige Werte eingeben.
                                    Raid-Countdown: 5–300 Sekunden;
                                    Start-Timeout: 15–600 Sekunden.
                                </p>
                            )}
                            <div className="flex flex-wrap gap-2">
                                <Button
                                    disabled={
                                        !valid || save.isPending || active
                                    }
                                    onClick={() => save.mutate("save")}
                                >
                                    Einstellungen speichern
                                </Button>
                                <Button
                                    disabled={
                                        !canStart || (manualRaid && !raidReady)
                                    }
                                    onClick={() => save.mutate("start")}
                                >
                                    Streamende starten
                                </Button>
                                <Button
                                    disabled={
                                        !canStart ||
                                        draft.plannedSeconds < 1 ||
                                        (plannedRaid && !raidReady)
                                    }
                                    onClick={() => save.mutate("plan")}
                                >
                                    Streamende planen
                                </Button>
                            </div>
                        </fieldset>
                    )}
                    <Button
                        disabled={active || save.isPending}
                        onClick={() => {
                            setDirty(false);
                            setEditor(undefined);
                            void client.invalidateQueries({
                                queryKey: configurationKey,
                            });
                        }}
                    >
                        Einstellungen neu laden
                    </Button>
                </>
            )}
            {runtime?.error && <p role="alert">{runtime.error}</p>}
            {(runtime?.warnings ?? []).map((warning, index) => (
                <p role="alert" key={index}>
                    {warning}
                </p>
            ))}
            {(editor?.warnings ?? []).map((warning, index) => (
                <p role="alert" key={`settings-${index}`}>
                    {warning}
                </p>
            ))}
            {save.error && <p role="alert">{String(save.error)}</p>}
            {control.error && <p role="alert">{String(control.error)}</p>}
            {resolve.error && <p role="alert">{String(resolve.error)}</p>}
            {onClose && (
                <Button
                    disabled={active && (!abortAvailable || pending)}
                    onClick={() => {
                        if (active) {
                            setClosing(true);
                            control.mutate("abort");
                        } else onClose();
                    }}
                >
                    {active ? "Abbrechen und schließen" : "Schließen"}
                </Button>
            )}
        </Card>
    );
}
