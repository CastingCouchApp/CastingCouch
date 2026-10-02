import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cloneSettings, type AppSettings } from "../../lib/app-settings";
import { tauriInvoke, queryKeys } from "../../lib/api";
import { Button } from "../../components/ui/button";
type Device = {
    id: string | null;
    name: string;
    is_active: boolean;
    is_restricted: boolean;
    type?: string;
};
export function SpotifyDevices() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const devices = useQuery({
        queryKey: ["spotify-devices"],
        queryFn: () =>
            tauriInvoke<{ devices: Device[] }>("spotify_query", {
                query: { query: "devices" },
            }),
        refetchInterval: 15000,
    });
    const save = useMutation({
        mutationFn: async (patch: Record<string, unknown>) => {
            if (!settings.data) throw new Error("Einstellungen fehlen");
            const next = cloneSettings(settings.data);
            next.Spotify = { ...next.Spotify, ...patch };
            return tauriInvoke("save_settings", {
                original: settings.data,
                settings: next,
            });
        },
        onSuccess: () =>
            client.invalidateQueries({ queryKey: queryKeys.settings }),
    });
    const activate = useMutation({
        mutationFn: () =>
            tauriInvoke("activate_spotify_device", { play: false }),
        onSuccess: () => {
            void client.invalidateQueries({ queryKey: ["spotify-devices"] });
            void client.invalidateQueries({ queryKey: ["spotify-playback"] });
        },
    });
    const transfer = useMutation({
        mutationFn: (deviceId: string) =>
            tauriInvoke("spotify_action", {
                action: { action: "transfer", deviceId },
            }),
        onSuccess: () => {
            void client.invalidateQueries({ queryKey: ["spotify-devices"] });
            void client.invalidateQueries({ queryKey: ["spotify-playback"] });
        },
    });
    const preferred = String(settings.data?.Spotify.PreferredDeviceId ?? "");
    const busy = save.isPending || activate.isPending || transfer.isPending;
    const usable = devices.data?.devices?.filter((d) => d.id) ?? [];
    return (
        <section className="space-y-3">
            <h3 className="font-semibold">Spotify-Geräte</h3>
            <label className="block">
                Spotify-Standardgerät
                <select
                    className="bg-panel border border-border rounded p-2 ml-2"
                    value={preferred}
                    disabled={!settings.data || busy}
                    onChange={(e) =>
                        save.mutate({ PreferredDeviceId: e.target.value })
                    }
                >
                    <option value="">Aktives Gerät verwenden</option>
                    {preferred && !usable.some((d) => d.id === preferred) && (
                        <option value={preferred}>
                            Gespeichertes Gerät (derzeit nicht erreichbar)
                        </option>
                    )}
                    {usable.map((d) => (
                        <option
                            key={d.id}
                            value={d.id!}
                            disabled={d.is_restricted}
                        >
                            {d.name}
                            {d.is_active ? " · Aktiv" : ""}
                            {d.is_restricted ? " · Eingeschränkt" : ""}
                        </option>
                    ))}
                </select>
            </label>
            <label className="flex gap-2">
                <input
                    type="checkbox"
                    disabled={!settings.data || busy}
                    checked={
                        settings.data?.Spotify
                            .UseActiveDeviceWhenPreferredUnavailable !== false
                    }
                    onChange={(e) =>
                        save.mutate({
                            UseActiveDeviceWhenPreferredUnavailable:
                                e.target.checked,
                        })
                    }
                />
                Aktives Gerät verwenden, wenn Standardgerät fehlt
            </label>
            <label className="flex gap-2">
                <input
                    type="checkbox"
                    disabled={!settings.data || busy}
                    checked={
                        settings.data?.Spotify.AutoTransferToPreferredDevice !==
                        false
                    }
                    onChange={(e) =>
                        save.mutate({
                            AutoTransferToPreferredDevice: e.target.checked,
                        })
                    }
                />
                Gerät beim Starten von Titeln oder Playlists aktivieren
            </label>
            <div className="flex gap-2">
                <Button
                    disabled={!settings.data || busy || !devices.data}
                    onClick={() => activate.mutate()}
                >
                    Standardgerät aktivieren
                </Button>
                <Button
                    disabled={busy || devices.isFetching}
                    onClick={() => void devices.refetch()}
                >
                    Geräte aktualisieren
                </Button>
            </div>
            <label className="block">
                Wiedergabegerät
                <select
                    className="bg-panel border border-border rounded p-2 ml-2"
                    value={usable.find((d) => d.is_active)?.id ?? ""}
                    disabled={busy || !devices.data}
                    onChange={(e) => {
                        if (e.target.value) transfer.mutate(e.target.value);
                    }}
                >
                    <option value="">Gerät auswählen</option>
                    {usable.map((d) => (
                        <option
                            key={d.id}
                            value={d.id!}
                            disabled={d.is_restricted}
                        >
                            {d.name}
                            {d.is_restricted ? " · Eingeschränkt" : ""}
                        </option>
                    ))}
                </select>
            </label>
            {devices.data?.devices?.length === 0 && (
                <p className="text-muted">
                    Spotify auf einem Gerät öffnen und kurz einen Titel starten.
                </p>
            )}
            {[
                settings.error,
                devices.error,
                save.error,
                activate.error,
                transfer.error,
            ]
                .filter(Boolean)
                .map((e, i) => (
                    <p role="alert" className="text-danger" key={i}>
                        {String(e)}
                    </p>
                ))}
        </section>
    );
}
