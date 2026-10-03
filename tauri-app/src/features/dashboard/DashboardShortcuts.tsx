import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { NumberSetting } from "../obs/SourceControls";
import { raidLiveDuration } from "../services/TwitchRaids";
import {
    FALLBACK_POLL_MS,
    listenTwitchRaids,
    queryKeys,
    tauriInvoke,
} from "../../lib/api";
import type { AppSettings } from "../../lib/app-settings";
import type { ObsControl, RaidTarget } from "../../lib/command-contract";

const selectClass = "rounded-md border border-border bg-input p-2";
export function DashboardConfiguredScenes({
    settings,
    enabled,
    currentScene,
}: {
    settings: AppSettings;
    enabled: boolean;
    currentScene?: string | null;
}) {
    const client = useQueryClient();
    const change = useMutation({
        mutationFn: (scene: string) => tauriInvoke("obs_set_scene", { scene }),
        onSuccess: () =>
            client.invalidateQueries({ queryKey: queryKeys.obsCurrentScene }),
    });
    const scenes = [
        { label: "Startszene", name: settings?.Obs?.StartScene },
        { label: "Liveszene", name: settings?.Obs?.LiveScene },
        { label: "Pauseszene", name: settings?.Obs?.PauseScene },
        { label: "Endszene", name: settings?.Obs?.EndScene },
    ];
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Szenen-Schnellwahl</h2>
            {!enabled && (
                <p className="text-sm text-text-secondary">
                    OBS nicht verbunden
                </p>
            )}
            <div className="grid grid-cols-2 gap-2">
                {scenes.map((scene) => (
                    <Button
                        key={scene.label}
                        variant="ghost"
                        disabled={
                            !enabled || !scene.name?.trim() || change.isPending
                        }
                        aria-pressed={
                            enabled &&
                            !!scene.name &&
                            scene.name.toLowerCase() ===
                                currentScene?.toLowerCase()
                        }
                        onClick={() => change.mutate(scene.name!)}
                        className="flex-col items-start text-left"
                    >
                        <span>{scene.label}</span>
                        <span className="max-w-full break-words text-xs">
                            {scene.name?.trim() || "Nicht konfiguriert"}
                        </span>
                    </Button>
                ))}
            </div>
            {change.error && <p role="alert">{String(change.error)}</p>}
            <Link className="text-sm text-brand underline" to="/settings">
                Szenen konfigurieren
            </Link>
        </Card>
    );
}

export function DashboardAudioMixer({ enabled }: { enabled: boolean }) {
    const [input, setInput] = useState("");
    const inputs = useQuery({
        queryKey: ["obs-audio", "inputs", enabled],
        queryFn: () =>
            tauriInvoke<{ inputs: { inputName: string }[] }>("obs_query", {
                query: { query: "inputs" },
            }),
        enabled,
        refetchInterval: FALLBACK_POLL_MS,
        retry: false,
    });
    const selected = (inputs.data?.inputs ?? []).some(
        (i) => i.inputName === input,
    )
        ? input
        : "";
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">OBS-Audiomixer</h2>
            {!enabled && <p>OBS nicht verbunden</p>}
            {inputs.error && <p role="alert">{String(inputs.error)}</p>}
            <label className="flex flex-wrap items-center gap-2">
                OBS-Audioquelle
                <select
                    className={selectClass}
                    disabled={!enabled || !inputs.data}
                    value={selected}
                    onChange={(e) => setInput(e.target.value)}
                >
                    <option value="">Quelle auswählen</option>
                    {inputs.data?.inputs.map((i) => (
                        <option key={i.inputName}>{i.inputName}</option>
                    ))}
                </select>
            </label>
            {selected ? (
                <AudioSource
                    key={selected}
                    input={selected}
                    enabled={enabled}
                />
            ) : (
                <p className="text-sm text-text-secondary">
                    Audioquelle auswählen
                </p>
            )}
            <Link className="text-sm text-brand underline" to="/services">
                OBS-Quellen verwalten
            </Link>
        </Card>
    );
}
function AudioSource({ input, enabled }: { input: string; enabled: boolean }) {
    const client = useQueryClient();
    const mute = useQuery({
        queryKey: ["obs-audio", input, "mute", enabled],
        queryFn: () =>
            tauriInvoke<{ inputMuted: boolean }>("obs_query", {
                query: { query: "mute", inputName: input },
            }),
        enabled,
        refetchInterval: FALLBACK_POLL_MS,
        retry: false,
    });
    const volume = useQuery({
        queryKey: ["obs-audio", input, "volume", enabled],
        queryFn: () =>
            tauriInvoke<{ inputVolumeDb: number }>("obs_query", {
                query: { query: "volume", inputName: input },
            }),
        enabled,
        refetchInterval: FALLBACK_POLL_MS,
        retry: false,
    });
    const apply = useMutation({
        mutationFn: (control: ObsControl) =>
            tauriInvoke("obs_control", { control }),
        onSettled: async () => {
            await Promise.all([
                client.invalidateQueries({ queryKey: ["obs-audio"] }),
                client.invalidateQueries({ queryKey: ["obs-management"] }),
            ]);
        },
    });
    const known =
        enabled &&
        !mute.isError &&
        !volume.isError &&
        typeof mute.data?.inputMuted === "boolean" &&
        typeof volume.data?.inputVolumeDb === "number" &&
        Number.isFinite(volume.data.inputVolumeDb);
    const errors = [
        ...new Set(
            [mute.error, volume.error, apply.error].filter(Boolean).map(String),
        ),
    ];
    return (
        <div className="space-y-3">
            {errors.map((error, index) => (
                <p role="alert" key={index}>
                    {error}
                </p>
            ))}
            <p className="text-sm text-text-secondary">
                {known
                    ? `${input}: ${mute.data!.inputMuted ? "Stumm" : "Aktiv"} · ${volume.data!.inputVolumeDb.toFixed(1)} dB`
                    : mute.isFetching || volume.isFetching
                      ? "Audiozustand wird abgefragt …"
                      : "Keine steuerbaren Audioeigenschaften verfügbar."}
            </p>
            <div className="flex flex-wrap gap-2">
                <Button
                    variant="ghost"
                    disabled={!known || apply.isPending}
                    onClick={() =>
                        apply.mutate({
                            action: "set_mute",
                            inputName: input,
                            inputMuted: true,
                        })
                    }
                >
                    Stummschalten
                </Button>
                <Button
                    variant="ghost"
                    disabled={!known || apply.isPending}
                    onClick={() =>
                        apply.mutate({
                            action: "set_mute",
                            inputName: input,
                            inputMuted: false,
                        })
                    }
                >
                    Stummschaltung aufheben
                </Button>
            </div>
            <NumberSetting
                label="OBS-Lautstärke (dB)"
                min={-100}
                max={26}
                step={0.1}
                value={known ? volume.data!.inputVolumeDb : undefined}
                disabled={!known || apply.isPending}
                apply={(db) =>
                    apply.mutateAsync({
                        action: "set_volume",
                        inputName: input,
                        inputVolumeDb: db,
                    })
                }
            />
        </div>
    );
}

export function DashboardRaidAssistant({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const [listenerError, setListenerError] = useState<string>();
    const [attempt, setAttempt] = useState(0);
    const settings = useQuery({
        queryKey: ["twitch-raid-settings"],
        queryFn: () =>
            tauriInvoke<{ selected: string }>("twitch_raid_settings"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const login = settings.data?.selected ?? "";
    const target = useQuery({
        queryKey: ["twitch-raid-target", login, enabled],
        queryFn: () =>
            tauriInvoke<RaidTarget | null>("twitch_raid_target", { login }),
        enabled: enabled && !!login,
        refetchInterval: 30000,
        retry: false,
    });
    useEffect(() => {
        let disposed = false;
        let stop: (() => void) | undefined;
        setListenerError(undefined);
        void listenTwitchRaids(() => {
            if (!disposed) {
                void client.invalidateQueries({
                    queryKey: ["twitch-raid-settings"],
                });
                void client.invalidateQueries({
                    queryKey: ["twitch-raid-target"],
                });
            }
        })
            .then((fn) => {
                if (disposed) fn();
                else stop = fn;
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            stop?.();
        };
    }, [client, attempt]);
    // A failed refresh must not present an old online status as current proof.
    const current =
        enabled && !target.isError && !target.isFetching ? target.data : null;
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">
                Raid-Assistent und Profile
            </h2>
            {!enabled && <p>Twitch nicht verbunden</p>}
            {[settings.error, target.error]
                .filter(Boolean)
                .map((error, index) => (
                    <p key={index} role="alert">
                        {String(error)}
                    </p>
                ))}
            {listenerError && (
                <div role="alert">
                    <p>Live-Aktualisierung nicht verfügbar: {listenerError}</p>
                    <Button
                        variant="ghost"
                        onClick={() => setAttempt((n) => n + 1)}
                    >
                        Raid-Aktualisierung erneut verbinden
                    </Button>
                </div>
            )}
            {!login && <p>Kein Raid-Ziel ausgewählt.</p>}
            {current && (
                <div className="space-y-1">
                    {current.profileImageUrl && (
                        <img
                            alt=""
                            src={current.profileImageUrl}
                            className="h-16 w-16 rounded-full"
                        />
                    )}
                    <p>
                        {current.displayName} ist{" "}
                        {current.isOnline ? "online" : "offline"}
                    </p>
                    {current.isOnline && (
                        <>
                            <p>
                                {current.viewerCount} Zuschauer ·{" "}
                                {current.category}
                            </p>
                            <p>{current.title}</p>
                            <p>
                                Live seit{" "}
                                {raidLiveDuration(current.startedAt) || "—"}
                            </p>
                        </>
                    )}
                </div>
            )}
            {enabled &&
                login &&
                !target.isError &&
                !target.isFetching &&
                target.data === null && <p>{login}: Kanal nicht gefunden</p>}
            {target.isFetching && <p role="status">Raid-Ziel wird geprüft …</p>}
            <Button
                variant="ghost"
                disabled={!enabled || !login || target.isFetching}
                onClick={() => void target.refetch()}
            >
                Raid-Ziel jetzt prüfen
            </Button>
            <Link className="block text-sm text-brand underline" to="/services">
                Raid-Ziele verwalten
            </Link>
            <div className="border-t border-border pt-3">
                <DashboardProfile />
            </div>
        </Card>
    );
}
export function DashboardProfile() {
    const client = useQueryClient();
    const [selected, setSelected] = useState("");
    const [message, setMessage] = useState("");
    const [warnings, setWarnings] = useState<string[]>([]);
    const profiles = useQuery({
        queryKey: ["profiles"],
        queryFn: () =>
            tauriInvoke<{
                profiles: { id: string; name: string }[];
                warnings: string[];
            }>("list_profiles"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    const profile = profiles.data?.profiles.find((p) => p.id === selected);
    const apply = useMutation({
        mutationFn: () =>
            tauriInvoke<{ saved: boolean; warnings: string[] }>(
                "apply_profile",
                { id: selected, original: settings.data },
            ),
        onSuccess: async (result) => {
            setWarnings(result.warnings ?? []);
            await client.invalidateQueries();
            setMessage("Profil angewendet.");
        },
    });
    return (
        <section className="space-y-2">
            <label className="flex flex-wrap items-center gap-2">
                Schnellprofil
                <select
                    className={selectClass}
                    value={profile ? selected : ""}
                    disabled={apply.isPending || !profiles.data}
                    onChange={(e) => {
                        setSelected(e.target.value);
                        setMessage("");
                        setWarnings([]);
                        apply.reset();
                    }}
                >
                    <option value="">Profil auswählen</option>
                    {profiles.data?.profiles.map((p) => (
                        <option key={p.id} value={p.id}>
                            {p.name}
                        </option>
                    ))}
                </select>
            </label>
            {profiles.data?.profiles.length === 0 && (
                <p className="text-sm text-text-secondary">
                    Noch keine Profile gespeichert.
                </p>
            )}
            <Button
                variant="ghost"
                disabled={!profile || !settings.data || apply.isPending}
                onClick={() => {
                    if (
                        profile &&
                        window.confirm(
                            `Profil „${profile.name}“ anwenden? Aktuelle und ungespeicherte Einstellungen werden ersetzt.`,
                        )
                    ) {
                        setMessage("");
                        setWarnings([]);
                        apply.mutate();
                    }
                }}
            >
                Profil anwenden
            </Button>
            {[profiles.error, settings.error, apply.error]
                .filter(Boolean)
                .map((error, index) => (
                    <p key={index} role="alert">
                        {String(error)}
                    </p>
                ))}
            {[...(profiles.data?.warnings ?? []), ...warnings].map(
                (warning, index) => (
                    <p key={index} role="alert">
                        {warning}
                    </p>
                ),
            )}
            {message && <p role="status">{message}</p>}
            <Link className="block text-sm text-brand underline" to="/settings">
                Profile verwalten
            </Link>
        </section>
    );
}
