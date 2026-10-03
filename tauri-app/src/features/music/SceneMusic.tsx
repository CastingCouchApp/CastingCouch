import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cloneSettings, type AppSettings } from "../../lib/app-settings";
import {
    FALLBACK_POLL_MS,
    listenMusicAutomation,
    queryKeys,
    tauriInvoke,
    type ObsSceneInfo,
    type MusicAutomationStatus,
} from "../../lib/api";
import type { MusicAction } from "../../lib/command-contract";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
type Rule = Record<string, unknown>;
const selectClass = "bg-panel border border-border rounded p-2";
function newRule(scene = "", action = "Resume", playlist = ""): Rule {
    return {
        Id: crypto.randomUUID().replace(/-/g, ""),
        Name: scene ? `Szenenmusik: ${scene}` : "Neue Spotify-Regel",
        Enabled: true,
        TriggerType: "ObsSceneChanged",
        TriggerValue: scene,
        ActionType: action,
        PlaylistUri: playlist,
        Shuffle: true,
        VolumePercent: 75,
        FadeEnabled: false,
        FadeMilliseconds: 500,
        DelaySeconds: 0,
    };
}
export function SceneMusic() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const scenes = useQuery({
        queryKey: queryKeys.obsScenes,
        queryFn: () => tauriInvoke<ObsSceneInfo[]>("obs_scenes"),
    });
    const status = useQuery({
        queryKey: ["music-automation-status"],
        queryFn: () =>
            tauriInvoke<MusicAutomationStatus>("music_automation_status"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    useEffect(() => {
        let disposed = false;
        let unlisten: (() => void) | undefined;
        listenMusicAutomation((next) => {
            void client.cancelQueries(
                { queryKey: ["music-automation-status"] },
                { revert: false },
            );
            client.setQueryData(["music-automation-status"], next);
        }).then((fn) => {
            if (disposed) fn();
            else unlisten = fn;
        });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [client]);
    const [draft, setDraft] = useState<AppSettings | null>(null);
    const [original, setOriginal] = useState<AppSettings | null>(null);
    const [testScene, setTestScene] = useState("");
    const value = draft ?? settings.data;
    const options = value?.Spotify as
        (AppSettings["Spotify"] & Record<string, unknown>) | undefined;
    const rules = Array.isArray(options?.AutomationRules)
        ? (options.AutomationRules as Rule[])
        : [];
    const visible = rules
        .map((rule, index) => ({ rule, index }))
        .filter(
            ({ rule }) =>
                String(rule.TriggerType ?? "ObsSceneChanged").toLowerCase() ===
                "obsscenechanged",
        );
    const save = useMutation({
        mutationFn: () =>
            tauriInvoke<{ saved: boolean; warnings: string[] }>(
                "save_settings",
                { original: original ?? settings.data, settings: draft },
            ),
        onSuccess: async () => {
            setDraft(null);
            setOriginal(null);
            await client.invalidateQueries({ queryKey: queryKeys.settings });
        },
    });
    const action = useMutation({
        mutationFn: (action: MusicAction) =>
            tauriInvoke("music_automation_action", { action }),
        onSettled: () =>
            client.invalidateQueries({ queryKey: ["music-automation-status"] }),
    });
    const stop = useMutation({
        mutationFn: () =>
            tauriInvoke("music_automation_action", {
                action: { action: "stop" },
            }),
        onSettled: () =>
            client.invalidateQueries({ queryKey: ["music-automation-status"] }),
    });
    const change = (patch: Record<string, unknown>) => {
        if (!value) return;
        if (!original) setOriginal(cloneSettings(value));
        const next = cloneSettings(value);
        Object.assign(next.Spotify, patch);
        setDraft(next);
    };
    const update = (index: number, patch: Record<string, unknown>) =>
        change({
            AutomationRules: rules.map((rule, i) =>
                i === index ? { ...rule, ...patch } : rule,
            ),
        });
    const check = (
        label: string,
        key: string,
        fallback: boolean,
        legacy?: string,
    ) => (
        <label className="flex items-center gap-2">
            <input
                type="checkbox"
                disabled={save.isPending}
                checked={
                    typeof options?.[key] === "boolean"
                        ? (options[key] as boolean)
                        : legacy &&
                            typeof value?.Workflow?.[legacy] === "boolean"
                          ? (value.Workflow[legacy] as boolean)
                          : fallback
                }
                onChange={(e) => change({ [key]: e.target.checked })}
            />
            {label}
        </label>
    );
    const number = (
        label: string,
        key: string,
        fallback: number,
        max: number,
    ) => (
        <label>
            {label}
            <Input
                type="number"
                min={0}
                max={max}
                disabled={save.isPending}
                value={Number(options?.[key] ?? fallback)}
                onChange={(e) =>
                    change({
                        [key]: Math.max(
                            0,
                            Math.min(max, Number(e.target.value) || 0),
                        ),
                    })
                }
            />
        </label>
    );
    const generate = () => {
        if (!value) return;
        const retained = rules.filter(
            (r) =>
                String(r.TriggerType ?? "ObsSceneChanged").toLowerCase() !==
                "obsscenechanged",
        );
        const playlist = String(options?.StartPlaylistUri ?? "");
        if (value.Obs.StartScene?.trim())
            retained.push({
                ...newRule(value.Obs.StartScene, "StartPlaylist", playlist),
                Shuffle: options?.ShuffleSelectedPlaylist === true,
            });
        if (value.Obs.LiveScene?.trim())
            retained.push(newRule(value.Obs.LiveScene));
        if (value.Obs.EndScene?.trim())
            retained.push(
                newRule(value.Obs.EndScene, "StartPlaylist", playlist),
            );
        change({ AutomationRules: retained });
    };
    if (!value)
        return settings.error ? (
            <p role="alert">{String(settings.error)}</p>
        ) : null;
    const names = Array.from(
        new Set(
            [
                ...(scenes.data ?? []).map((s) => s.name),
                ...visible.map(({ rule }) => String(rule.TriggerValue ?? "")),
                value.Obs.StartScene,
                value.Obs.LiveScene,
                value.Obs.EndScene,
            ].filter(Boolean),
        ),
    );
    const busy = action.isPending || stop.isPending;
    return (
        <Card className="space-y-4">
            <h2 className="text-lg font-semibold">Szenenmusik und Fades</h2>
            <p className="text-sm text-muted-foreground">
                Gespeicherte Regeln reagieren auf OBS-Szenen. Ein neuer
                Szenenwechsel oder eine manuelle Playeraktion bricht laufende
                Verzögerungen und Fades ab.
            </p>
            {check(
                "Szenenregeln automatisch ausführen",
                "SmartAutomationEnabled",
                true,
            )}
            <label className="block">
                Startplaylist-URI
                <Input
                    disabled={save.isPending}
                    value={String(options?.StartPlaylistUri ?? "")}
                    placeholder="spotify:playlist:…"
                    onChange={(e) =>
                        change({ StartPlaylistUri: e.target.value })
                    }
                />
            </label>
            <div className="grid gap-3 md:grid-cols-2">
                {check(
                    "Playlist bei Streamstart starten",
                    "StartOnStreamStart",
                    true,
                    "AutoStartSpotifyPlaylist",
                )}
                {check(
                    "Musik in Endszene starten",
                    "PlayEndMusic",
                    false,
                    "AutoPlayEndMusic",
                )}
                {check(
                    "Musik bei Streamende pausieren",
                    "PauseOnStreamEnd",
                    true,
                    "PauseSpotifyOnStreamEnd",
                )}
                {check(
                    "Startplaylist zufällig abspielen",
                    "ShuffleSelectedPlaylist",
                    false,
                )}
                {number("Startlautstärke (%)", "StartVolumePercent", 100, 100)}
                {check("Startplaylist weich einblenden", "FadeInEnabled", true)}
                {number("Einblenden (Sekunden)", "FadeInSeconds", 3, 60)}
                {check(
                    "Bei Streamende weich ausblenden",
                    "FadeOutEnabled",
                    true,
                )}
                {number("Ausblenden (Sekunden)", "FadeOutSeconds", 3, 60)}
                {check(
                    "Nach manuellem Ausblenden pausieren",
                    "PauseAfterFadeOut",
                    true,
                )}
                {check(
                    "Live-Szene: Pause-Regel durch Lautstärke ersetzen",
                    "SetVolumeOnLiveTransition",
                    true,
                )}
                {check(
                    "Live-Szene: Pause-Regel unverändert ausführen",
                    "MuteOnLiveTransition",
                    false,
                )}
                {number(
                    "Lautstärke in Live-Szene (%)",
                    "LiveVolumePercent",
                    75,
                    100,
                )}
            </div>
            <datalist id="music-scene-names">
                {names.map((name) => (
                    <option key={name} value={name} />
                ))}
            </datalist>
            {visible.map(({ rule, index }, position) => (
                <fieldset
                    key={String(rule.Id ?? index)}
                    aria-label={`Musikregel ${position + 1}`}
                    className="border border-border rounded p-3 space-y-3"
                    disabled={save.isPending}
                >
                    <legend>Musikregel {position + 1}</legend>
                    <div className="grid gap-3 md:grid-cols-3">
                        <label>
                            Regelname
                            <Input
                                value={String(rule.Name ?? "Spotify-Regel")}
                                onChange={(e) =>
                                    update(index, { Name: e.target.value })
                                }
                            />
                        </label>
                        <label className="flex gap-2 items-center">
                            <input
                                type="checkbox"
                                checked={rule.Enabled !== false}
                                onChange={(e) =>
                                    update(index, { Enabled: e.target.checked })
                                }
                            />
                            Regel aktiv
                        </label>
                        <label>
                            OBS-Szene
                            <Input
                                list="music-scene-names"
                                value={String(rule.TriggerValue ?? "")}
                                onChange={(e) =>
                                    update(index, {
                                        TriggerValue: e.target.value,
                                    })
                                }
                            />
                        </label>
                        <label className="flex flex-col">
                            Musikaktion
                            <select
                                className={selectClass}
                                value={String(rule.ActionType ?? "Resume")}
                                onChange={(e) =>
                                    update(index, {
                                        ActionType: e.target.value,
                                    })
                                }
                            >
                                {[
                                    ["StartPlaylist", "Playlist starten"],
                                    ["Resume", "Wiedergabe fortsetzen"],
                                    ["Pause", "Pausieren"],
                                    ["SetVolume", "Lautstärke setzen"],
                                ].map(([key, label]) => (
                                    <option key={key} value={key}>
                                        {label}
                                    </option>
                                ))}
                                {Boolean(rule.ActionType) &&
                                    ![
                                        "StartPlaylist",
                                        "Resume",
                                        "Pause",
                                        "SetVolume",
                                    ].includes(String(rule.ActionType)) && (
                                        <option value={String(rule.ActionType)}>
                                            {String(rule.ActionType)}{" "}
                                            (importiert)
                                        </option>
                                    )}
                            </select>
                        </label>
                        {String(rule.ActionType).toLowerCase() ===
                            "startplaylist" && (
                            <>
                                <label>
                                    Regel-Playlist-URI
                                    <Input
                                        value={String(rule.PlaylistUri ?? "")}
                                        onChange={(e) =>
                                            update(index, {
                                                PlaylistUri: e.target.value,
                                            })
                                        }
                                    />
                                </label>
                                <label className="flex gap-2 items-center">
                                    <input
                                        type="checkbox"
                                        checked={rule.Shuffle !== false}
                                        onChange={(e) =>
                                            update(index, {
                                                Shuffle: e.target.checked,
                                            })
                                        }
                                    />
                                    Regel-Zufallswiedergabe
                                </label>
                            </>
                        )}
                        <label>
                            Regellautstärke (%)
                            <Input
                                type="number"
                                min={0}
                                max={100}
                                value={Number(rule.VolumePercent ?? 75)}
                                onChange={(e) =>
                                    update(index, {
                                        VolumePercent: Math.max(
                                            0,
                                            Math.min(
                                                100,
                                                Number(e.target.value) || 0,
                                            ),
                                        ),
                                    })
                                }
                            />
                        </label>
                        <label className="flex gap-2 items-center">
                            <input
                                type="checkbox"
                                checked={rule.FadeEnabled === true}
                                onChange={(e) =>
                                    update(index, {
                                        FadeEnabled: e.target.checked,
                                    })
                                }
                            />
                            Regel-Fade aktiv
                        </label>
                        <label>
                            Regel-Fade (Millisekunden)
                            <Input
                                type="number"
                                min={0}
                                max={60000}
                                value={Number(rule.FadeMilliseconds ?? 500)}
                                onChange={(e) =>
                                    update(index, {
                                        FadeMilliseconds: Math.max(
                                            0,
                                            Math.min(
                                                60000,
                                                Number(e.target.value) || 0,
                                            ),
                                        ),
                                    })
                                }
                            />
                        </label>
                        <label>
                            Verzögerung (Sekunden)
                            <Input
                                type="number"
                                min={0}
                                max={3600}
                                value={Number(rule.DelaySeconds ?? 0)}
                                onChange={(e) =>
                                    update(index, {
                                        DelaySeconds: Math.max(
                                            0,
                                            Math.min(
                                                3600,
                                                Number(e.target.value) || 0,
                                            ),
                                        ),
                                    })
                                }
                            />
                        </label>
                    </div>
                    <Button
                        onClick={() =>
                            change({
                                AutomationRules: rules.filter(
                                    (_, i) => i !== index,
                                ),
                            })
                        }
                    >
                        Regel löschen
                    </Button>
                </fieldset>
            ))}
            <div className="flex flex-wrap gap-2">
                <Button
                    disabled={save.isPending}
                    onClick={() =>
                        change({ AutomationRules: [...rules, newRule()] })
                    }
                >
                    Musikregel hinzufügen
                </Button>
                <Button disabled={save.isPending} onClick={generate}>
                    Standardregeln als Entwurf erstellen
                </Button>
                <Button
                    disabled={!draft || save.isPending}
                    onClick={() => save.mutate()}
                >
                    Szenenmusik speichern
                </Button>
                {draft && (
                    <Button
                        disabled={save.isPending}
                        onClick={() => {
                            setDraft(null);
                            setOriginal(null);
                            save.reset();
                        }}
                    >
                        Entwurf verwerfen
                    </Button>
                )}
            </div>
            {rules.length > visible.length && (
                <p className="text-sm">
                    Weitere importierte Trigger bleiben erhalten.
                </p>
            )}
            {save.error && <p role="alert">{String(save.error)}</p>}
            {save.isSuccess && <p>Szenenmusik gespeichert.</p>}
            {save.data?.warnings?.map((w) => (
                <p role="alert" key={w}>
                    {w}
                </p>
            ))}
            <div className="flex flex-wrap items-end gap-2">
                <label className="flex flex-col">
                    Testszene
                    <select
                        className={selectClass}
                        value={testScene}
                        onChange={(e) => setTestScene(e.target.value)}
                    >
                        <option value="">Szene wählen</option>
                        {names.map((name) => (
                            <option key={name} value={name}>
                                {name}
                            </option>
                        ))}
                    </select>
                </label>
                <Button
                    disabled={!testScene || busy || !!draft}
                    onClick={() =>
                        action.mutate({
                            action: "scene",
                            scene: testScene,
                            force: true,
                        })
                    }
                >
                    Gespeicherte Regeln testen
                </Button>
                <Button
                    disabled={busy || !!draft}
                    onClick={() => action.mutate({ action: "start_playlist" })}
                >
                    Startplaylist starten
                </Button>
                <Button
                    disabled={busy || !!draft}
                    onClick={() => action.mutate({ action: "fade_in" })}
                >
                    Einblenden
                </Button>
                <Button
                    disabled={busy || !!draft}
                    onClick={() => action.mutate({ action: "fade_out" })}
                >
                    Ausblenden
                </Button>
                <Button disabled={stop.isPending} onClick={() => stop.mutate()}>
                    Musikaktion abbrechen
                </Button>
            </div>
            {draft && (
                <p>
                    Tests und Wiedergabe verwenden gespeicherte Einstellungen.
                    Entwurf zuerst speichern oder verwerfen.
                </p>
            )}
            {action.error && <p role="alert">{String(action.error)}</p>}
            {stop.error && <p role="alert">{String(stop.error)}</p>}
            {scenes.error && (
                <p role="alert">
                    OBS-Szenen nicht verfügbar: {String(scenes.error)}
                </p>
            )}
            {status.error && (
                <p role="alert">
                    Musikstatus nicht verfügbar: {String(status.error)}
                </p>
            )}
            <p aria-live="polite">
                {status.data?.running
                    ? `Musikaktion läuft: ${status.data.action}`
                    : "Keine laufende Musikaktion."}
            </p>
            {!!status.data?.history?.length && (
                <details>
                    <summary>Musikaktionsverlauf</summary>
                    <ul className="space-y-1">
                        {status.data.history.map((entry, i) => (
                            <li
                                key={`${entry.at}-${i}`}
                                className={
                                    entry.success ? "" : "text-destructive"
                                }
                            >
                                {new Date(entry.at).toLocaleTimeString()} ·{" "}
                                {entry.rule}: {entry.message}
                            </li>
                        ))}
                    </ul>
                </details>
            )}
        </Card>
    );
}
