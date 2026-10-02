import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { cloneSettings, type AppSettings } from "../../lib/app-settings";
import { queryKeys, tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";

export function MusicAutomation() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const [draft, setDraft] = useState<AppSettings | null>(null);
    const [original, setOriginal] = useState<AppSettings | null>(null);
    const value = draft ?? settings.data;
    const spotify = value?.Spotify as
        (AppSettings["Spotify"] & Record<string, unknown>) | undefined;
    const save = useMutation({
        mutationFn: () =>
            tauriInvoke<{ saved: boolean; warnings: string[] }>(
                "save_settings",
                { settings: draft, original: original ?? settings.data },
            ),
        onSuccess: async () => {
            setDraft(null);
            setOriginal(null);
            await client.invalidateQueries({ queryKey: queryKeys.settings });
        },
    });
    const change = (key: string, nextValue: unknown) => {
        if (!value) return;
        if (!original) setOriginal(cloneSettings(value));
        const next = cloneSettings(value);
        Object.assign(next.Spotify, { [key]: nextValue });
        if (key === "MuteDuringAlerts")
            Object.assign(next.Spotify, {
                AlertDuckingMode: nextValue ? "Duck" : "None",
            });
        setDraft(next);
    };
    if (!value)
        return settings.error ? (
            <p role="alert">{String(settings.error)}</p>
        ) : null;
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Musik während Alerts</h2>
            <p className="text-sm text-muted-foreground">
                Spotify bleibt bis zum Ende aller Alerts und der Warteschlange
                abgesenkt. Danach wird die vorherige Lautstärke
                wiederhergestellt.
            </p>
            {(
                [
                    ["SmartAutomationEnabled", "Musikautomatik aktiv", true],
                    ["MuteDuringAlerts", "Während Alerts absenken", true],
                    ["FadeDuringAlerts", "Lautstärke weich überblenden", true],
                ] as const
            ).map(([key, label, fallback]) => (
                <label key={key} className="flex items-center gap-2">
                    <input
                        type="checkbox"
                        checked={
                            (key !== "MuteDuringAlerts" ||
                                spotify?.AlertDuckingMode !== "None") &&
                            (typeof spotify?.[key] === "boolean"
                                ? (spotify[key] as boolean)
                                : fallback)
                        }
                        onChange={(e) => change(key, e.target.checked)}
                    />
                    {label}
                </label>
            ))}
            <div className="grid gap-3 md:grid-cols-3">
                {(
                    [
                        [
                            "AlertMuteVolumePercent",
                            "Lautstärke während Alerts (%)",
                            75,
                            100,
                        ],
                        [
                            "AlertFadeOutMilliseconds",
                            "Absenken (Millisekunden)",
                            500,
                            5000,
                        ],
                        [
                            "AlertFadeInMilliseconds",
                            "Wiederherstellen (Millisekunden)",
                            500,
                            5000,
                        ],
                    ] as const
                ).map(([key, label, fallback, max]) => (
                    <label key={key}>
                        {label}
                        <Input
                            type="number"
                            min={0}
                            max={max}
                            value={
                                typeof spotify?.[key] === "number"
                                    ? (spotify[key] as number)
                                    : fallback
                            }
                            onChange={(e) =>
                                change(
                                    key,
                                    Math.max(
                                        0,
                                        Math.min(
                                            max,
                                            Number(e.target.value) || 0,
                                        ),
                                    ),
                                )
                            }
                        />
                    </label>
                ))}
            </div>
            <Button
                disabled={!draft || save.isPending}
                onClick={() => save.mutate()}
            >
                Musikautomatik speichern
            </Button>
            {save.error && <p role="alert">{String(save.error)}</p>}
            {save.isSuccess && <p>Musikautomatik gespeichert.</p>}
            {save.data?.warnings?.map((warning) => (
                <p role="alert" key={warning}>
                    {warning}
                </p>
            ))}
        </Card>
    );
}
