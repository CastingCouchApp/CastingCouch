import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { cloneSettings, type AppSettings } from "../../lib/app-settings";
import { queryKeys, tauriInvoke, type ServiceStatus } from "../../lib/api";

type OverlayStatus = {
    visible: boolean;
    overlayEnabled: boolean;
    obsAudioMuted: boolean | null;
    overlayError: string | null;
};
type ObsInputs = {
    inputs: Array<{
        inputName: string;
        inputKind: string;
        unversionedInputKind?: string;
    }>;
};
type ObsItems = { sceneItems: Array<{ sourceName: string }> };
export function MusicOverlaySettings() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const [draft, setDraft] = useState<{
        original: AppSettings;
        next: AppSettings;
    } | null>(null);
    const current = draft?.next ?? settings.data;
    const options = current?.Spotify as
        (AppSettings["Spotify"] & Record<string, unknown>) | undefined;
    const flag = (key: string, fallback: boolean) =>
        typeof options?.[key] === "boolean"
            ? (options[key] as boolean)
            : fallback;
    const text = (key: string, fallback: string) =>
        typeof options?.[key] === "string"
            ? (options[key] as string)
            : fallback;
    const status = useQuery({
        queryKey: ["music-overlay"],
        queryFn: () => tauriInvoke<OverlayStatus>("music_overlay_snapshot"),
        refetchInterval: 2000,
    });
    const services = useQuery({
        queryKey: queryKeys.services,
        queryFn: () => tauriInvoke<ServiceStatus[]>("service_statuses"),
        refetchInterval: 5000,
    });
    const connected =
        services.data?.some((s) => s.id === "obs" && s.state === "connected") ??
        false;
    const scenes = useQuery({
        queryKey: queryKeys.obsScenes,
        queryFn: () => tauriInvoke<Array<{ name: string }>>("obs_scenes"),
        enabled: connected,
        refetchInterval: 10000,
    });
    const inputs = useQuery({
        queryKey: ["music-overlay-inputs"],
        queryFn: () =>
            tauriInvoke<ObsInputs>("obs_query", { query: { query: "inputs" } }),
        enabled: connected,
        refetchInterval: 10000,
    });
    const scene = text("OverlayObsScene", "");
    const items = useQuery({
        queryKey: ["music-overlay-items", scene],
        queryFn: () =>
            tauriInvoke<ObsItems>("obs_query", {
                query: { query: "scene_items", sceneName: scene },
            }),
        enabled: connected && !!scene.trim(),
        refetchInterval: 10000,
    });
    const save = useMutation({
        mutationFn: async () => {
            if (!draft) throw Error("Keine Änderungen");
            return tauriInvoke<{ saved: boolean; warnings: string[] }>(
                "save_settings",
                { original: draft.original, settings: draft.next },
            );
        },
        onSuccess: async () => {
            setDraft(null);
            await Promise.all(
                [queryKeys.settings, ["music-overlay"]].map((queryKey) =>
                    client.invalidateQueries({ queryKey }),
                ),
            );
        },
    });
    const change = (key: string, value: unknown) => {
        if (!current) return;
        const next = cloneSettings(current);
        Object.assign(next.Spotify, { [key]: value });
        setDraft({ original: draft?.original ?? cloneSettings(current), next });
    };
    if (!current)
        return settings.error ? (
            <p role="alert">{String(settings.error)}</p>
        ) : null;
    const browserNames = new Set(
        (inputs.data?.inputs ?? [])
            .filter(
                (i) =>
                    i.inputKind === "browser_source" ||
                    i.unversionedInputKind === "browser_source",
            )
            .map((i) => i.inputName),
    );
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Musik im Overlay</h2>
            <p className="text-sm text-text-secondary">
                Titel, Interpret, Cover und Fortschritt bleiben vollständig
                verfügbar. Einzelne Widgets können ihre Darstellung zusätzlich
                konfigurieren.
            </p>
            {(
                [
                    [
                        "OverlayEnabled",
                        "Musikdaten ins Overlay schreiben",
                        true,
                    ],
                    [
                        "SmartAutomationEnabled",
                        "Automatisches Ausblenden aktiv",
                        true,
                    ],
                    [
                        "OverlayHideWhenPaused",
                        "Bei Pause ausblenden (3 Sekunden Nachlauf)",
                        false,
                    ],
                    ["OverlayHideWhenMuted", "Bei Mute ausblenden", true],
                    [
                        "OverlayMuteDetectionSpotifyVolume",
                        "Spotify-Lautstärke zur Mute-Erkennung verwenden",
                        true,
                    ],
                    [
                        "OverlayMuteDetectionObsSource",
                        "OBS-Audioquelle zur Mute-Erkennung verwenden",
                        true,
                    ],
                ] as const
            ).map(([key, label, fallback]) => (
                <label key={key} className="flex items-center gap-2">
                    <input
                        type="checkbox"
                        checked={flag(key, fallback)}
                        onChange={(e) => change(key, e.target.checked)}
                    />
                    {label}
                </label>
            ))}
            <p className="text-sm text-text-secondary">
                Vorhandene Musikdaten bleiben erhalten, wenn der Writer
                deaktiviert wird. Musikautomatik steuert auch vorhandene
                Musikregeln und Alert-Ducking.
            </p>
            <div className="grid gap-3 md:grid-cols-3">
                <label>
                    OBS-Audioquelle für Mute-Erkennung
                    <Input
                        list="music-overlay-audio-sources"
                        value={text("OverlayObsAudioSource", "Spotify")}
                        onChange={(e) =>
                            change("OverlayObsAudioSource", e.target.value)
                        }
                    />
                </label>
                <label>
                    OBS-Overlay-Szene
                    <Input
                        list="music-overlay-scenes"
                        value={scene}
                        onChange={(e) =>
                            change("OverlayObsScene", e.target.value)
                        }
                    />
                </label>
                <label>
                    OBS-Overlay-Quelle
                    <Input
                        list="music-overlay-browser-sources"
                        value={text("OverlayObsSource", "ccs_spotify")}
                        onChange={(e) =>
                            change("OverlayObsSource", e.target.value)
                        }
                    />
                </label>
            </div>
            <datalist id="music-overlay-audio-sources">
                {inputs.data?.inputs?.map((input) => (
                    <option key={input.inputName} value={input.inputName} />
                ))}
            </datalist>
            <datalist id="music-overlay-scenes">
                {scenes.data?.map((s) => (
                    <option key={s.name} value={s.name} />
                ))}
            </datalist>
            <datalist id="music-overlay-browser-sources">
                {items.data?.sceneItems
                    ?.filter((i) => browserNames.has(i.sourceName))
                    .map((i) => (
                        <option key={i.sourceName} value={i.sourceName} />
                    ))}
            </datalist>
            <p className="text-sm text-text-secondary">
                Die konfigurierte OBS-Quelle wird als Ganzes ein- und
                ausgeblendet. Für ein Canvas mit weiteren Widgets nur die
                JSON-Sichtbarkeit nutzen und die OBS-Szene leer lassen.
            </p>
            {status.data && (
                <p role="status">
                    {!status.data.overlayEnabled
                        ? "Musik-Writer deaktiviert"
                        : status.data.visible
                          ? "Musik-Overlay sichtbar"
                          : "Musik-Overlay ausgeblendet"}
                </p>
            )}
            {flag("OverlayMuteDetectionObsSource", true) && (
                <p className="text-sm text-text-secondary">
                    OBS-Mute:{" "}
                    {status.data?.obsAudioMuted === null ||
                    status.data?.obsAudioMuted === undefined
                        ? "unbekannt"
                        : status.data.obsAudioMuted
                          ? "stumm"
                          : "nicht stumm"}
                </p>
            )}
            {[
                settings.error,
                status.error,
                status.data?.overlayError,
                save.error,
            ]
                .filter(Boolean)
                .map((error, index) => (
                    <p role="alert" key={index} className="text-red-400">
                        {String(error)}
                    </p>
                ))}
            {(scenes.error || inputs.error || items.error) && (
                <p className="text-sm text-text-secondary">
                    OBS-Auswahl nicht verfügbar. Namen können weiterhin
                    eingegeben werden.
                </p>
            )}
            {save.data?.warnings?.map((warning) => (
                <p role="alert" key={warning}>
                    {warning}
                </p>
            ))}
            <Button
                disabled={!draft || save.isPending}
                onClick={() => save.mutate()}
            >
                Musik-Overlay speichern
            </Button>
        </Card>
    );
}
