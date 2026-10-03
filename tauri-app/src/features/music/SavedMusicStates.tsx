import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { open, save } from "@tauri-apps/plugin-dialog";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { cloneSettings, type AppSettings } from "../../lib/app-settings";
import {
    FALLBACK_POLL_MS,
    listenMusicStatesChanged,
    queryKeys,
    tauriInvoke,
} from "../../lib/api";
import { MusicStateRecovery } from "./MusicStateRecovery";
import {
    musicStateAction,
    musicStateKey,
    selectClass,
    type MusicStateSnapshot,
} from "./music-state-types";
import type { MusicStateAction } from "../../lib/command-contract";

const numericPrefs = [
    ["SavedStateMaxAgeMinutes", "Zustandsalter (Minuten)", 180, 1, 10080],
    [
        "SavedStateCleanupIntervalMinutes",
        "Bereinigungsintervall (Minuten)",
        15,
        1,
        1440,
    ],
    ["HealthCheckIntervalSeconds", "Geräteprüfung (Sekunden)", 30, 5, 300],
] as const;
const booleanPrefs = [
    ["SavedStateCleanupOnStartup", "Alte Zustände beim Start bereinigen", true],
    ["SavedStateCleanupOnSave", "Alte Zustände beim Sichern bereinigen", true],
    [
        "SavedStateCleanupIntervalEnabled",
        "Zustände regelmäßig bereinigen",
        false,
    ],
    ["HealthMonitorEnabled", "Spotify-Gerät überwachen", true],
    ["AutoRecoverPlayback", "Fehlendes Gerät automatisch aktivieren", true],
] as const;

export function SavedMusicStates() {
    const client = useQueryClient();
    const data = useQuery({
        queryKey: musicStateKey,
        queryFn: () => tauriInvoke<MusicStateSnapshot>("music_state_snapshot"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    useEffect(() => {
        let disposed = false,
            unlisten: (() => void) | undefined;
        listenMusicStatesChanged(() => {
            void client.invalidateQueries({ queryKey: musicStateKey });
        }).then((fn) => {
            if (disposed) fn();
            else unlisten = fn;
        });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [client]);
    const [group, setGroup] = useState("Standard"),
        [fade, setFade] = useState(0);
    const [selected, setSelected] = useState<string[]>([]),
        [note, setNote] = useState("");
    const [filters, setFilters] = useState<{
        SearchText: string;
        ActionFilterIndex: number;
        SortIndex: number;
        FavoritesOnly: boolean;
    } | null>(null);
    const [draft, setDraft] = useState<AppSettings | null>(null),
        [original, setOriginal] = useState<AppSettings | null>(null);
    const [confirm, setConfirm] = useState<{
        label: string;
        action: MusicStateAction;
    } | null>(null);
    const [message, setMessage] = useState("");
    const operation = useMutation({
        mutationFn: (task: () => Promise<void>) => task(),
        onSettled: () => client.invalidateQueries({ queryKey: musicStateKey }),
    });
    const stop = useMutation({
        mutationFn: () =>
            tauriInvoke("music_automation_action", {
                action: { action: "stop" },
            }),
    });
    const run = (task: () => Promise<void>) => {
        setMessage("");
        operation.mutate(task);
    };
    const act = (action: MusicStateAction) =>
        run(async () => {
            await musicStateAction(action);
            setMessage("Aktion abgeschlossen.");
        });
    const snapshot = data.data,
        history = snapshot?.history;
    const picked = selected.filter((entry) => history?.Entries.includes(entry));
    const favorite =
        picked.length === 1 && !!history?.FavoriteEntries.includes(picked[0]);
    const pref = (draft ?? settings.data)?.Spotify as
        (AppSettings["Spotify"] & Record<string, unknown>) | undefined;
    function editPref(key: string, value: unknown) {
        if (!settings.data) return;
        if (!draft) setOriginal(cloneSettings(settings.data));
        const next = cloneSettings(draft ?? settings.data);
        Object.assign(next.Spotify, { [key]: value });
        setDraft(next);
    }
    async function exportHistory(csv: boolean, selection: boolean) {
        const path = await save({
            defaultPath: `spotify-history.${csv ? "csv" : "json"}`,
            filters: [
                {
                    name: csv ? "CSV" : "JSON",
                    extensions: [csv ? "csv" : "json"],
                },
            ],
        });
        if (path) {
            await musicStateAction({
                action: "history_export",
                path,
                csv,
                entries: selection ? picked : null,
            });
            setMessage("Verlauf exportiert.");
        }
    }
    const currentFilters = filters ?? history;
    const validSettings = numericPrefs.every(([key, , fallback, min, max]) => {
        const n = Number(pref?.[key] ?? fallback);
        return Number.isInteger(n) && n >= min && n <= max;
    });
    return (
        <div className="space-y-4">
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">
                    Gespeicherte Spotify-Zustände
                </h2>
                <p className="text-sm text-text-secondary">
                    Sichert Titel, Kontext, Position und Player-Einstellungen.
                    Beim Wiederherstellen wird das bevorzugte Spotify-Gerät
                    verwendet. Erfolgreich wiederhergestellte Zustände werden
                    verbraucht.
                </p>
                {(data.error ||
                    settings.error ||
                    operation.error ||
                    stop.error) && (
                    <p role="alert" className="text-danger">
                        {String(
                            operation.error ??
                                stop.error ??
                                data.error ??
                                settings.error,
                        )}
                    </p>
                )}
                {message && <p role="status">{message}</p>}
                {operation.isPending && <p role="status">Aktion läuft…</p>}
                <Button
                    variant="ghost"
                    onClick={() => stop.mutate()}
                    disabled={stop.isPending}
                >
                    Wiederherstellung abbrechen
                </Button>
                <fieldset
                    disabled={operation.isPending || !snapshot}
                    className="space-y-3"
                >
                    <div className="flex flex-wrap items-end gap-3">
                        <label>
                            Zustandsgruppe
                            <Input
                                value={group}
                                maxLength={160}
                                onChange={(e) => setGroup(e.target.value)}
                            />
                        </label>
                        <Button
                            onClick={() => act({ action: "capture", group })}
                        >
                            Wiedergabe sichern
                        </Button>
                        <label>
                            Restore-Fade (Sekunden)
                            <Input
                                type="number"
                                min={0}
                                max={300}
                                value={fade}
                                onChange={(e) =>
                                    setFade(Number(e.target.value))
                                }
                            />
                        </label>
                        <Button
                            variant="ghost"
                            onClick={() => act({ action: "cleanup" })}
                        >
                            Alte Zustände bereinigen
                        </Button>
                        <Button
                            variant="danger"
                            disabled={
                                !Object.keys(snapshot?.states ?? {}).length
                            }
                            onClick={() =>
                                setConfirm({
                                    label: "Alle gespeicherten Zustände verwerfen?",
                                    action: { action: "discard_all" },
                                })
                            }
                        >
                            Alle Zustände verwerfen
                        </Button>
                    </div>
                    {!Object.keys(snapshot?.states ?? {}).length && (
                        <p>Keine gespeicherten Zustände.</p>
                    )}
                    {Object.entries(snapshot?.states ?? {}).map(
                        ([name, state]) => {
                            const expired =
                                Date.now() - Date.parse(state.SavedAtUtc) >
                                Number(pref?.SavedStateMaxAgeMinutes ?? 180) *
                                    60000;
                            return (
                                <div
                                    key={name}
                                    role="group"
                                    aria-label={`Zustand: ${name}`}
                                    className="rounded border border-border p-3 space-y-2"
                                >
                                    <strong>{name}</strong>
                                    <p>
                                        {state.Track?.Name ||
                                            "Unbekannter Titel"}{" "}
                                        ·{" "}
                                        {state.Track?.Artist ||
                                            "Unbekannter Interpret"}
                                    </p>
                                    <p className="text-sm text-text-secondary">
                                        {Math.floor(state.ProgressMs / 1000)} s
                                        · {state.VolumePercent}% · Shuffle{" "}
                                        {state.ShuffleEnabled ? "an" : "aus"} ·
                                        Repeat {state.RepeatMode} ·{" "}
                                        {state.WasPlaying
                                            ? "Wiedergabe"
                                            : "Pausiert"}
                                    </p>
                                    <p className="text-sm">
                                        Gesichert:{" "}
                                        {new Date(
                                            state.SavedAtUtc,
                                        ).toLocaleString()}{" "}
                                        {expired && "· Abgelaufen"}
                                    </p>
                                    <div className="flex gap-2">
                                        <Button
                                            disabled={
                                                !Number.isInteger(fade) ||
                                                fade < 0 ||
                                                fade > 300
                                            }
                                            onClick={() =>
                                                act({
                                                    action: "restore",
                                                    group: name,
                                                    fadeSeconds: fade,
                                                })
                                            }
                                        >
                                            {name} wiederherstellen
                                        </Button>
                                        <Button
                                            variant="danger"
                                            onClick={() =>
                                                setConfirm({
                                                    label: `Zustand „${name}“ verwerfen?`,
                                                    action: {
                                                        action: "discard",
                                                        group: name,
                                                    },
                                                })
                                            }
                                        >
                                            {name} verwerfen
                                        </Button>
                                    </div>
                                </div>
                            );
                        },
                    )}
                </fieldset>
                {confirm && (
                    <div
                        role="group"
                        aria-label="Löschen bestätigen"
                        className="space-x-2"
                    >
                        <p>{confirm.label}</p>
                        <Button
                            variant="danger"
                            disabled={operation.isPending}
                            onClick={() =>
                                run(async () => {
                                    await musicStateAction(confirm.action);
                                    setConfirm(null);
                                })
                            }
                        >
                            Verwerfen bestätigen
                        </Button>
                        <Button
                            variant="ghost"
                            onClick={() => setConfirm(null)}
                        >
                            Abbrechen
                        </Button>
                    </div>
                )}
            </Card>
            <Card className="space-y-3">
                <h3 className="font-semibold">
                    Zustandseinstellungen und Geräteüberwachung
                </h3>
                <p>{snapshot?.health.detail || "Status wird geladen…"}</p>
                {snapshot?.health.error && (
                    <p role="alert" className="text-danger">
                        {snapshot.health.error}
                    </p>
                )}
                {snapshot?.health.lastRecovery && (
                    <p>
                        Letzte Reaktivierung:{" "}
                        {new Date(
                            snapshot.health.lastRecovery,
                        ).toLocaleString()}
                    </p>
                )}
                <p className="text-sm text-text-secondary">
                    Automatische Reaktivierung startet keine Musik. Laufende
                    Szenenaktionen, Fades und Alerts haben Vorrang.
                </p>
                <fieldset
                    disabled={operation.isPending || !settings.data}
                    className="space-y-3"
                >
                    <div className="flex flex-wrap gap-3">
                        {numericPrefs.map(
                            ([key, label, fallback, min, max]) => (
                                <label key={key}>
                                    {label}
                                    <Input
                                        type="number"
                                        value={Number(pref?.[key] ?? fallback)}
                                        min={min}
                                        max={max}
                                        onChange={(e) =>
                                            editPref(
                                                key,
                                                Number(e.target.value),
                                            )
                                        }
                                    />
                                </label>
                            ),
                        )}
                    </div>
                    {booleanPrefs.map(([key, label, fallback]) => (
                        <label key={key} className="flex gap-2 items-center">
                            <input
                                type="checkbox"
                                checked={Boolean(pref?.[key] ?? fallback)}
                                onChange={(e) =>
                                    editPref(key, e.target.checked)
                                }
                            />
                            {label}
                        </label>
                    ))}
                    {!validSettings && (
                        <p role="alert">
                            Bitte gültige Intervalle und ein gültiges
                            Zustandsalter eingeben.
                        </p>
                    )}
                    <Button
                        disabled={!draft || !validSettings}
                        onClick={() =>
                            run(async () => {
                                await tauriInvoke("save_settings", {
                                    original,
                                    settings: draft,
                                });
                                setDraft(null);
                                setOriginal(null);
                                await client.invalidateQueries({
                                    queryKey: queryKeys.settings,
                                });
                                setMessage("Einstellungen gespeichert.");
                            })
                        }
                    >
                        Zustandseinstellungen speichern
                    </Button>
                    {draft && (
                        <Button
                            variant="ghost"
                            onClick={() => {
                                setDraft(null);
                                setOriginal(null);
                            }}
                        >
                            Entwurf verwerfen
                        </Button>
                    )}
                </fieldset>
            </Card>
            {history && (
                <Card className="space-y-3">
                    <h3 className="font-semibold">Zustandsverlauf</h3>
                    <p>
                        {history.SavedCount} gesichert · {history.RestoredCount}{" "}
                        wiederhergestellt · {history.DiscardedCount} verworfen ·{" "}
                        {history.CleanupCount} bereinigt
                    </p>
                    <fieldset
                        disabled={operation.isPending}
                        className="space-y-3"
                    >
                        <div className="flex flex-wrap gap-3">
                            <label>
                                Verlauf durchsuchen
                                <Input
                                    value={currentFilters?.SearchText ?? ""}
                                    onChange={(e) =>
                                        setFilters({
                                            ...currentFilters!,
                                            SearchText: e.target.value,
                                        })
                                    }
                                />
                            </label>
                            <label>
                                Verlaufsaktion
                                <select
                                    className={selectClass}
                                    value={
                                        currentFilters?.ActionFilterIndex ?? 0
                                    }
                                    onChange={(e) =>
                                        setFilters({
                                            ...currentFilters!,
                                            ActionFilterIndex: Number(
                                                e.target.value,
                                            ),
                                        })
                                    }
                                >
                                    {[
                                        "Alle",
                                        "Gesichert",
                                        "Wiederhergestellt",
                                        "Verworfen",
                                        "Bereinigt",
                                    ].map((label, i) => (
                                        <option key={label} value={i}>
                                            {label}
                                        </option>
                                    ))}
                                </select>
                            </label>
                            <label>
                                Verlauf sortieren
                                <select
                                    className={selectClass}
                                    value={currentFilters?.SortIndex ?? 0}
                                    onChange={(e) =>
                                        setFilters({
                                            ...currentFilters!,
                                            SortIndex: Number(e.target.value),
                                        })
                                    }
                                >
                                    {[
                                        "Neueste zuerst",
                                        "Älteste zuerst",
                                        "Aktion",
                                        "Gruppe",
                                    ].map((label, i) => (
                                        <option key={label} value={i}>
                                            {label}
                                        </option>
                                    ))}
                                </select>
                            </label>
                            <label className="flex gap-2 items-center">
                                <input
                                    type="checkbox"
                                    checked={
                                        currentFilters?.FavoritesOnly ?? false
                                    }
                                    onChange={(e) =>
                                        setFilters({
                                            ...currentFilters!,
                                            FavoritesOnly: e.target.checked,
                                        })
                                    }
                                />
                                Nur Favoriten
                            </label>
                            <Button
                                disabled={!filters}
                                onClick={() =>
                                    run(async () => {
                                        await musicStateAction({
                                            action: "history_filters",
                                            filters,
                                        });
                                        setFilters(null);
                                    })
                                }
                            >
                                Verlaufsfilter anwenden
                            </Button>
                        </div>
                        <div className="max-h-72 overflow-auto space-y-2">
                            {snapshot.visibleHistory.map((entry, index) => (
                                <label
                                    key={`${entry}-${index}`}
                                    className="flex gap-2 items-start rounded border border-border p-2"
                                >
                                    <input
                                        type="checkbox"
                                        aria-label={`Verlauf auswählen: ${entry}`}
                                        checked={picked.includes(entry)}
                                        onChange={(e) => {
                                            setSelected(
                                                e.target.checked
                                                    ? [...picked, entry]
                                                    : picked.filter(
                                                          (s) => s !== entry,
                                                      ),
                                            );
                                            setNote(
                                                e.target.checked
                                                    ? (history.Notes[entry] ??
                                                          "")
                                                    : "",
                                            );
                                        }}
                                    />
                                    <span>
                                        {history.FavoriteEntries.includes(
                                            entry,
                                        ) && "★ "}
                                        {entry}
                                        {history.Notes[entry] && (
                                            <small className="block text-text-secondary">
                                                {history.Notes[entry]}
                                            </small>
                                        )}
                                    </span>
                                </label>
                            ))}
                        </div>
                        {!snapshot.visibleHistory.length && (
                            <p>Keine passenden Verlaufseinträge.</p>
                        )}
                        <p>{picked.length} ausgewählt</p>
                        <label>
                            Verlaufsnotiz
                            <Input
                                disabled={picked.length !== 1}
                                value={note}
                                maxLength={4096}
                                onChange={(e) => setNote(e.target.value)}
                            />
                        </label>
                        <div className="flex flex-wrap gap-2">
                            <Button
                                disabled={picked.length !== 1}
                                onClick={() =>
                                    act({
                                        action: "history_edit",
                                        entries: picked,
                                        favorite: null,
                                        note,
                                        remove: false,
                                    })
                                }
                            >
                                Notiz speichern
                            </Button>
                            <Button
                                disabled={picked.length !== 1}
                                onClick={() =>
                                    act({
                                        action: "history_edit",
                                        entries: picked,
                                        favorite: !favorite,
                                        note: null,
                                        remove: false,
                                    })
                                }
                            >
                                {favorite
                                    ? "Favorit entfernen"
                                    : "Favorit setzen"}
                            </Button>
                            <Button
                                disabled={!picked.length}
                                variant="ghost"
                                onClick={() =>
                                    run(async () => {
                                        await navigator.clipboard.writeText(
                                            picked.join("\n"),
                                        );
                                        setMessage("Auswahl kopiert.");
                                    })
                                }
                            >
                                Auswahl kopieren
                            </Button>
                            <Button
                                disabled={!picked.length}
                                variant="danger"
                                onClick={() =>
                                    setConfirm({
                                        label: `${picked.length} Verlaufseinträge löschen?`,
                                        action: {
                                            action: "history_edit",
                                            entries: picked,
                                            favorite: null,
                                            note: null,
                                            remove: true,
                                        },
                                    })
                                }
                            >
                                Auswahl löschen
                            </Button>
                            <Button
                                variant="danger"
                                disabled={!history.Entries.length}
                                onClick={() =>
                                    setConfirm({
                                        label: "Gesamten Verlauf löschen? Zähler bleiben erhalten.",
                                        action: { action: "history_clear" },
                                    })
                                }
                            >
                                Verlauf leeren
                            </Button>
                        </div>
                        <div className="flex flex-wrap gap-2">
                            {[false, true].flatMap((selection) =>
                                [false, true].map((csv) => (
                                    <Button
                                        key={`${selection}-${csv}`}
                                        variant="ghost"
                                        disabled={selection && !picked.length}
                                        onClick={() =>
                                            run(() =>
                                                exportHistory(csv, selection),
                                            )
                                        }
                                    >
                                        {selection ? "Auswahl" : "Verlauf"} als{" "}
                                        {csv ? "CSV" : "JSON"} exportieren
                                    </Button>
                                )),
                            )}
                            <Button
                                variant="ghost"
                                onClick={() =>
                                    run(async () => {
                                        const path = await open({
                                            multiple: false,
                                            filters: [
                                                {
                                                    name: "Spotify-Verlauf",
                                                    extensions: ["json"],
                                                },
                                            ],
                                        });
                                        if (typeof path === "string")
                                            setConfirm({
                                                label: `Verlauf durch „${path}“ ersetzen? Eine Sicherung wird vorher erstellt.`,
                                                action: {
                                                    action: "history_import",
                                                    path,
                                                },
                                            });
                                    })
                                }
                            >
                                Verlauf importieren
                            </Button>
                        </div>
                    </fieldset>
                </Card>
            )}
            {snapshot && (
                <MusicStateRecovery
                    data={snapshot}
                    run={run}
                    pending={operation.isPending}
                />
            )}
        </div>
    );
}
