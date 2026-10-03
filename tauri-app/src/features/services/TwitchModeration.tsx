import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    FALLBACK_POLL_MS,
    listenTwitchModeration,
    tauriInvoke,
    type ModerationResult,
} from "../../lib/api";
import type { ModerationAction } from "../../lib/command-contract";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";

export function useModerationAction() {
    const client = useQueryClient();
    return useMutation({
        mutationFn: (action: ModerationAction) =>
            tauriInvoke<ModerationResult>("twitch_moderate", { action }),
        onSettled: () => {
            void client.invalidateQueries({ queryKey: ["twitch-moderation"] });
            void client.invalidateQueries({
                queryKey: ["twitch-chat-history"],
            });
        },
    });
}
export function TwitchModeration({
    enabled,
    selectedUser,
    selectionVersion = 0,
}: {
    enabled: boolean;
    selectedUser?: string;
    selectionVersion?: number;
}) {
    const client = useQueryClient();
    const [user, setUser] = useState(selectedUser ?? "");
    const [minutes, setMinutes] = useState("10");
    const [reason, setReason] = useState("");
    const [message, setMessage] = useState("");
    const [listenerError, setListenerError] = useState<string>();
    useEffect(() => {
        if (selectedUser) setUser(selectedUser);
    }, [selectedUser, selectionVersion]);
    const log = useQuery({
        queryKey: ["twitch-moderation"],
        queryFn: () =>
            tauriInvoke<{ entries: string[] }>("twitch_moderation_snapshot"),
        refetchInterval: FALLBACK_POLL_MS,
    });
    useEffect(() => {
        let disposed = false;
        let unlisten: (() => void) | undefined;
        void listenTwitchModeration(() => {
            if (!disposed)
                void client.invalidateQueries({
                    queryKey: ["twitch-moderation"],
                });
        })
            .then((fn) => {
                if (disposed) fn();
                else {
                    unlisten = fn;
                    void client.invalidateQueries({
                        queryKey: ["twitch-moderation"],
                    });
                }
            })
            .catch((error) => {
                if (!disposed) setListenerError(String(error));
            });
        return () => {
            disposed = true;
            unlisten?.();
        };
    }, [client]);
    const action = useModerationAction();
    const local = useMutation({
        mutationFn: async (kind: "clear" | "export") => {
            setMessage("");
            if (kind === "clear") {
                await tauriInvoke("clear_twitch_moderation_view");
                await client.invalidateQueries({
                    queryKey: ["twitch-moderation"],
                });
                return;
            }
            const { save } = await import("@tauri-apps/plugin-dialog");
            const path = await save({
                defaultPath: "twitch-moderation.txt",
                filters: [
                    {
                        name: "Moderationsprotokoll",
                        extensions: ["txt", "log"],
                    },
                ],
            });
            if (!path) return;
            await tauriInvoke("export_twitch_moderation_log", { path });
            setMessage(`Protokoll exportiert: ${path}`);
            try {
                const { openPath } = await import("@tauri-apps/plugin-opener");
                await openPath(path);
            } catch (error) {
                throw new Error(
                    `Protokoll exportiert; Datei konnte nicht geöffnet werden: ${String(error)}`,
                );
            }
        },
    });
    const busy = action.isPending || local.isPending;
    const validMinutes =
        /^\d+$/.test(minutes) &&
        Number(minutes) > 0 &&
        Number(minutes) <= 2147483647;
    const errors = [
        listenerError,
        log.error,
        action.error,
        local.error,
        ...(action.data?.warnings ?? []),
    ].filter(Boolean);
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Twitch-Moderation</h2>
            {errors.map((error, index) => (
                <p role="alert" key={index}>
                    {String(error)}
                </p>
            ))}
            {(message || action.data?.message) && (
                <p role="status">{message || action.data?.message}</p>
            )}
            <fieldset disabled={!enabled || busy} className="space-y-3">
                <label className="block">
                    Moderationsbenutzer
                    <Input
                        value={user}
                        onChange={(e) => setUser(e.target.value)}
                        maxLength={200}
                    />
                </label>
                <label className="block">
                    Timeout in Minuten
                    <Input
                        type="number"
                        min="1"
                        max="2147483647"
                        step="1"
                        value={minutes}
                        onChange={(e) => setMinutes(e.target.value)}
                    />
                </label>
                <label className="block">
                    Moderationsgrund
                    <Input
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                        maxLength={500}
                    />
                </label>
                <div className="flex flex-wrap gap-2">
                    {[
                        [1, "1 Min."],
                        [10, "10 Min."],
                        [60, "1 Std."],
                        [1440, "24 Std."],
                    ].map(([value, label]) => (
                        <Button
                            key={value}
                            variant="ghost"
                            onClick={() => setMinutes(String(value))}
                        >
                            {label}
                        </Button>
                    ))}
                </div>
                <div className="flex flex-wrap gap-2">
                    <Button
                        disabled={!user.trim() || !validMinutes}
                        onClick={() =>
                            action.mutate({
                                action: "timeout",
                                user,
                                byId: false,
                                minutes: Number(minutes),
                                reason,
                            })
                        }
                    >
                        Timeout anwenden
                    </Button>
                    <Button
                        variant="danger"
                        disabled={!user.trim()}
                        onClick={() =>
                            action.mutate({
                                action: "ban",
                                user,
                                byId: false,
                                reason,
                            })
                        }
                    >
                        Benutzer bannen
                    </Button>
                    <Button
                        disabled={!user.trim()}
                        onClick={() =>
                            action.mutate({
                                action: "unban",
                                user,
                                byId: false,
                            })
                        }
                    >
                        Ban oder Timeout aufheben
                    </Button>
                </div>
            </fieldset>
            <h3 className="font-semibold">Moderationsprotokoll</h3>
            <div className="flex flex-wrap gap-2">
                <Button disabled={busy} onClick={() => local.mutate("export")}>
                    Moderationsprotokoll exportieren
                </Button>
                <Button
                    variant="ghost"
                    disabled={busy}
                    onClick={() => local.mutate("clear")}
                >
                    Protokollansicht leeren
                </Button>
            </div>
            <ul
                aria-label="Moderationsprotokoll"
                className="max-h-40 overflow-auto space-y-1 text-sm"
            >
                {log.data?.entries.map((entry, index) => (
                    <li className="whitespace-pre-wrap break-words" key={index}>
                        {entry}
                    </li>
                ))}
            </ul>
        </Card>
    );
}
