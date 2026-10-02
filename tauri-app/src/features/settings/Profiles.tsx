import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { open, save } from "@tauri-apps/plugin-dialog";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { queryKeys, tauriInvoke } from "../../lib/api";
import type { AppSettings } from "../../lib/app-settings";

type Profile = {
    id: string;
    name: string;
    description: string;
    updatedAt: string;
};
type ProfileList = { profiles: Profile[]; warnings: string[] };
const profilesKey = ["profiles"] as const;

export function Profiles({ onApplied }: { onApplied?: () => void } = {}) {
    const client = useQueryClient();
    const list = useQuery({
        queryKey: profilesKey,
        queryFn: () => tauriInvoke<ProfileList>("list_profiles"),
    });
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const [name, setName] = useState("");
    const [description, setDescription] = useState("");
    const [editing, setEditing] = useState<string | null>(null);
    const [message, setMessage] = useState("");
    const [warnings, setWarnings] = useState<string[]>([]);
    const mutation = useMutation({
        mutationFn: async (operation: () => Promise<void>) => operation(),
    });
    function run(operation: () => Promise<void>) {
        setMessage("");
        setWarnings([]);
        mutation.mutate(operation);
    }
    async function refreshProfiles() {
        await client.invalidateQueries({ queryKey: profilesKey });
    }
    function resetEditor() {
        setName("");
        setDescription("");
        setEditing(null);
    }

    return (
        <Card className="mt-4 space-y-3">
            <h2 className="text-lg font-medium">App-Profile</h2>
            <p className="text-sm text-zinc-400">
                Profile speichern Einstellungen. Anmeldungen bleiben erhalten;
                Layout- und Mediendateien sind im Profil nicht enthalten.
            </p>
            {(mutation.error || list.error) && (
                <p role="alert">{String(mutation.error || list.error)}</p>
            )}
            {[...(list.data?.warnings ?? []), ...warnings].map(
                (warning, index) => (
                    <p role="alert" key={`${index}-${warning}`}>
                        {warning}
                    </p>
                ),
            )}
            {message && <p role="status">{message}</p>}
            <fieldset disabled={mutation.isPending} className="space-y-3">
                <label className="block text-sm">
                    Profilname
                    <Input
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        maxLength={160}
                    />
                </label>
                <label className="block text-sm">
                    Profilbeschreibung
                    <Input
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        maxLength={4096}
                    />
                </label>
                <div className="flex flex-wrap gap-2">
                    <Button
                        disabled={!name.trim()}
                        onClick={() =>
                            run(async () => {
                                if (editing)
                                    await tauriInvoke("update_profile", {
                                        id: editing,
                                        name: name.trim(),
                                        description,
                                    });
                                else
                                    await tauriInvoke("create_profile", {
                                        name: name.trim(),
                                        description,
                                    });
                                resetEditor();
                                await refreshProfiles();
                                setMessage("Profil gespeichert.");
                            })
                        }
                    >
                        {editing
                            ? "Profil speichern"
                            : "Aus aktuellen Einstellungen erstellen"}
                    </Button>
                    {editing && (
                        <Button variant="ghost" onClick={resetEditor}>
                            Bearbeitung abbrechen
                        </Button>
                    )}
                    <Button
                        variant="ghost"
                        onClick={() =>
                            run(async () => {
                                const path = await open({
                                    multiple: false,
                                    filters: [
                                        {
                                            name: "App-Profil",
                                            extensions: ["ccsprofile", "json"],
                                        },
                                    ],
                                });
                                if (typeof path !== "string") return;
                                await tauriInvoke("import_profile", { path });
                                await refreshProfiles();
                                setMessage("Profil importiert.");
                            })
                        }
                    >
                        Profil importieren
                    </Button>
                </div>
                {list.isPending && <p>Profile werden geladen…</p>}
                {list.data?.profiles.length === 0 && (
                    <p className="text-sm text-zinc-400">
                        Noch keine Profile gespeichert.
                    </p>
                )}
                {list.data?.profiles.map((profile) => (
                    <div
                        key={profile.id}
                        className="rounded-md border border-white/15 p-3 space-y-2"
                    >
                        <p className="font-medium">{profile.name}</p>
                        {profile.description && (
                            <p className="text-sm text-zinc-400">
                                {profile.description}
                            </p>
                        )}
                        <div className="flex flex-wrap gap-2">
                            <Button
                                disabled={!settings.data}
                                onClick={() => {
                                    if (
                                        !settings.data ||
                                        !window.confirm(
                                            `Profil „${profile.name}“ anwenden? Aktuelle und ungespeicherte Einstellungen werden ersetzt.`,
                                        )
                                    )
                                        return;
                                    const original = settings.data;
                                    run(async () => {
                                        const result = await tauriInvoke<{
                                            saved: boolean;
                                            warnings: string[];
                                        }>("apply_profile", {
                                            id: profile.id,
                                            original,
                                        });
                                        setWarnings(result?.warnings ?? []);
                                        await client.invalidateQueries();
                                        onApplied?.();
                                        setMessage("Profil angewendet.");
                                    });
                                }}
                            >
                                Profil anwenden
                            </Button>
                            <Button
                                variant="ghost"
                                onClick={() => {
                                    setEditing(profile.id);
                                    setName(profile.name);
                                    setDescription(profile.description);
                                }}
                            >
                                Profil bearbeiten
                            </Button>
                            <Button
                                variant="ghost"
                                onClick={() =>
                                    run(async () => {
                                        const filename =
                                            profile.name
                                                .replace(
                                                    /[<>:"/\\|?*\u0000-\u001f]/g,
                                                    "_",
                                                )
                                                .replace(/\.+$/, "") ||
                                            "Profil";
                                        const selected = await save({
                                            defaultPath: `${filename}.ccsprofile`,
                                            filters: [
                                                {
                                                    name: "App-Profil",
                                                    extensions: ["ccsprofile"],
                                                },
                                            ],
                                        });
                                        if (!selected) return;
                                        const path = selected
                                            .toLowerCase()
                                            .endsWith(".ccsprofile")
                                            ? selected
                                            : `${selected}.ccsprofile`;
                                        await tauriInvoke("export_profile", {
                                            id: profile.id,
                                            path,
                                        });
                                        setMessage("Profil exportiert.");
                                    })
                                }
                            >
                                Profil exportieren
                            </Button>
                            <Button
                                variant="ghost"
                                onClick={() => {
                                    if (
                                        !window.confirm(
                                            `Profil „${profile.name}“ löschen?`,
                                        )
                                    )
                                        return;
                                    run(async () => {
                                        await tauriInvoke("delete_profile", {
                                            id: profile.id,
                                        });
                                        if (editing === profile.id)
                                            resetEditor();
                                        await refreshProfiles();
                                        setMessage("Profil gelöscht.");
                                    });
                                }}
                            >
                                Profil löschen
                            </Button>
                        </div>
                    </div>
                ))}
            </fieldset>
        </Card>
    );
}
