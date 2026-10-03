import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { cloneSettings, type AppSettings } from "../../lib/app-settings";
import { queryKeys, tauriInvoke, type YtmNowPlaying } from "../../lib/api";
type BridgeRuntime = {
    running: boolean;
    port: number | null;
    configuredPort: number;
    installUrl: string | null;
    bookmarklet: string | null;
    error: string | null;
    snapshot: YtmNowPlaying;
};
const runtimeKey = ["ytm-runtime"] as const;
export function YouTubeMusicSetup() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const runtime = useQuery({
        queryKey: runtimeKey,
        queryFn: () => tauriInvoke<BridgeRuntime>("ytm_runtime_status"),
        refetchInterval: 2000,
    });
    const [draft, setDraft] = useState<{
            original: AppSettings;
            next: AppSettings;
        } | null>(null),
        [message, setMessage] = useState(""),
        [copyError, setCopyError] = useState("");
    const [manual, setManual] = useState(false),
        [copying, setCopying] = useState(false);
    const data = draft?.next ?? settings.data;
    const edit = (key: string, value: number | boolean) => {
        if (!settings.data) return;
        setDraft((previous) => {
            const original =
                previous?.original ?? cloneSettings(settings.data!);
            const next = cloneSettings(previous?.next ?? settings.data!);
            next.YouTubeMusic = { ...next.YouTubeMusic, [key]: value };
            return { original, next };
        });
    };
    const operation = useMutation({
        mutationFn: (action: () => Promise<unknown>) => action(),
        onSettled: () => client.invalidateQueries({ queryKey: runtimeKey }),
    });
    const save = useMutation({
        mutationFn: async () => {
            if (!draft) throw new Error("Kein Entwurf vorhanden");
            return tauriInvoke<{ warnings?: string[] }>("save_settings", {
                original: draft.original,
                settings: draft.next,
            });
        },
        onSuccess: async (result) => {
            setDraft(null);
            setMessage(
                result.warnings?.length
                    ? result.warnings.join(" ")
                    : "Bridge-Einstellungen gespeichert. Bei geändertem Port das Bookmarklet neu einrichten.",
            );
            await Promise.all([
                client.invalidateQueries({ queryKey: queryKeys.settings }),
                client.invalidateQueries({ queryKey: runtimeKey }),
            ]);
        },
    });
    const bridge = runtime.data,
        snapshot = bridge?.snapshot;
    const port = Number(data?.YouTubeMusic.BridgePort ?? 43831),
        timeout = Number(data?.YouTubeMusic.StateTimeoutSeconds ?? 12);
    const valid =
        Number.isInteger(port) &&
        port >= 1 &&
        port <= 65535 &&
        Number.isInteger(timeout) &&
        timeout >= 3 &&
        timeout <= 120;
    const copy = async () => {
        if (!bridge?.bookmarklet) return;
        setCopyError("");
        setMessage("");
        setManual(true);
        setCopying(true);
        try {
            await navigator.clipboard.writeText(bridge.bookmarklet);
            setMessage("Bookmarklet kopiert.");
        } catch (error) {
            setCopyError(String(error));
        } finally {
            setCopying(false);
        }
    };
    return (
        <Card className="space-y-4">
            <h2 className="text-lg font-semibold">YouTube Music</h2>
            {snapshot?.title && (
                <div className="flex gap-3">
                    {snapshot.coverUrl && (
                        <img
                            src={snapshot.coverUrl}
                            alt={`Cover von ${snapshot.title}`}
                            className="h-16 w-16 rounded object-cover"
                        />
                    )}
                    <div className="min-w-0">
                        <p className="font-semibold break-words">
                            {snapshot.title}
                        </p>
                        <p>{snapshot.artist}</p>
                        <p className="text-text-secondary">{snapshot.album}</p>
                    </div>
                </div>
            )}
            <p>{snapshot?.statusText || "Bridge-Status wird geladen …"}</p>
            {bridge?.running && (
                <p className="text-sm text-text-secondary">
                    Bridge-Port: {bridge.port}
                </p>
            )}
            {[
                settings.error,
                runtime.error,
                bridge?.error,
                operation.error,
                save.error,
                copyError,
            ]
                .filter(Boolean)
                .map((error, index) => (
                    <p role="alert" key={index} className="text-red-400">
                        {String(error)}
                    </p>
                ))}
            {message && <p role="status">{message}</p>}
            <div className="flex flex-wrap gap-2">
                <Button
                    disabled={operation.isPending || bridge?.running}
                    onClick={() =>
                        operation.mutate(() => tauriInvoke("ytm_connect"))
                    }
                >
                    YouTube Music verbinden
                </Button>
                <Button
                    variant="ghost"
                    disabled={operation.isPending || !bridge?.running}
                    onClick={() =>
                        operation.mutate(() => tauriInvoke("ytm_disconnect"))
                    }
                >
                    YouTube Music trennen
                </Button>
            </div>
            <div className="flex flex-wrap gap-2">
                {[
                    ["previous", "Vorheriger YouTube-Titel"],
                    [
                        snapshot?.isPlaying ? "pause" : "play",
                        snapshot?.isPlaying
                            ? "YouTube Music pausieren"
                            : "YouTube Music abspielen",
                    ],
                    ["next", "Nächster YouTube-Titel"],
                ].map(([command, label]) => (
                    <Button
                        key={command}
                        disabled={!snapshot?.connected || operation.isPending}
                        onClick={() =>
                            operation.mutate(() =>
                                tauriInvoke("ytm_command", { command }),
                            )
                        }
                    >
                        {label}
                    </Button>
                ))}
            </div>
            <section className="space-y-2">
                <h3 className="font-semibold">Bookmarklet einrichten</h3>
                <ol className="list-decimal space-y-1 pl-5 text-sm text-text-secondary">
                    <li>
                        Bridge verbinden und die Install-Seite im Browser
                        öffnen.
                    </li>
                    <li>
                        Den Link dort in die Lesezeichenleiste ziehen oder das
                        kopierte Bookmarklet als Lesezeichenadresse speichern.
                    </li>
                    <li>
                        Auf music.youtube.com das Lesezeichen ausführen und den
                        Tab offen lassen. Browserfreigaben für die lokale
                        Verbindung gegebenenfalls bestätigen.
                    </li>
                </ol>
                <div className="flex flex-wrap items-center gap-3">
                    {bridge?.running && bridge.installUrl && (
                        <a
                            href={bridge.installUrl}
                            target="_blank"
                            rel="noreferrer"
                            className="underline"
                        >
                            Install-Seite öffnen
                        </a>
                    )}
                    <Button
                        variant="ghost"
                        disabled={!bridge?.bookmarklet || copying}
                        onClick={() => void copy()}
                    >
                        Bookmarklet kopieren
                    </Button>
                </div>
                {manual && bridge?.bookmarklet && (
                    <textarea
                        aria-label="Bookmarklet zum manuellen Kopieren"
                        readOnly
                        value={bridge.bookmarklet}
                        className="w-full rounded border border-border bg-input p-2 text-sm"
                        rows={3}
                        onFocus={(event) => event.currentTarget.select()}
                    />
                )}
            </section>
            {data && (
                <section className="space-y-3">
                    <h3 className="font-semibold">Bridge-Einstellungen</h3>
                    <label className="block">
                        YouTube-Music-Port
                        <Input
                            aria-label="YouTube-Music-Port"
                            type="number"
                            min={1}
                            max={65535}
                            value={port}
                            onChange={(event) =>
                                edit("BridgePort", Number(event.target.value))
                            }
                        />
                    </label>
                    <label className="block">
                        Bookmarklet-Timeout (Sekunden)
                        <Input
                            aria-label="Bookmarklet-Timeout (Sekunden)"
                            type="number"
                            min={3}
                            max={120}
                            value={timeout}
                            onChange={(event) =>
                                edit(
                                    "StateTimeoutSeconds",
                                    Number(event.target.value),
                                )
                            }
                        />
                    </label>
                    <label className="flex gap-2">
                        <input
                            type="checkbox"
                            checked={data.YouTubeMusic.AutoConnect !== false}
                            onChange={(event) =>
                                edit("AutoConnect", event.target.checked)
                            }
                        />
                        YouTube Music beim Appstart verbinden
                    </label>
                    <p className="text-sm text-text-secondary">
                        Autostart gilt bei ausgewähltem YouTube-Music-Provider.
                        Nach einem Portwechsel benötigt der Browser ein neues
                        Bookmarklet.
                    </p>
                    {!valid && (
                        <p role="alert">
                            Port: 1–65535; Timeout: 3–120 Sekunden.
                        </p>
                    )}
                    <Button
                        disabled={!draft || !valid || save.isPending}
                        onClick={() => {
                            setMessage("");
                            save.mutate();
                        }}
                    >
                        Bridge-Einstellungen speichern
                    </Button>
                </section>
            )}
        </Card>
    );
}
