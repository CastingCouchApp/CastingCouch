import { useState } from "react";
import { open, save } from "@tauri-apps/plugin-dialog";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { tauriInvoke } from "../../lib/api";
import {
    defaultRestore,
    musicStateAction,
    optionLabels,
    selectClass,
    type BackupPreview,
    type MusicStateSnapshot,
    type ProfilePreview,
    type RestoreOptions,
    type RestoreProfile,
    type RunOperation,
} from "./music-state-types";

function profileOptions(profile: RestoreProfile): RestoreOptions {
    return Object.fromEntries(
        Object.keys(defaultRestore).map((key) => [key, Boolean(profile[key])]),
    ) as RestoreOptions;
}
export function MusicStateRecovery({
    data,
    run,
    pending,
}: {
    data: MusicStateSnapshot;
    run: RunOperation;
    pending: boolean;
}) {
    const [preview, setPreview] = useState<{
        id: string;
        value: BackupPreview;
    } | null>(null);
    const [options, setOptions] = useState<RestoreOptions>({
        ...defaultRestore,
    });
    const [profileName, setProfileName] = useState("");
    const [editing, setEditing] = useState<RestoreProfile | null>(null);
    const [profileReview, setProfileReview] = useState<ProfilePreview | null>(
        null,
    );
    const [importActions, setImportActions] = useState<string[]>([]);
    const [deleting, setDeleting] = useState<{
        kind: "backup" | "profile";
        name: string;
    } | null>(null);
    const valid = Object.entries(options).some(
        ([key, value]) => key !== "MergeEntries" && value,
    );
    return (
        <Card className="space-y-4">
            <h3 className="font-semibold">
                Verlaufssicherungen und Wiederherstellungsprofile
            </h3>
            <p className="text-sm text-text-secondary">
                Sicherungen enthalten den Verlauf und seine Metadaten.
                Wiedergabezustände und App-Einstellungen bleiben bei der
                Verlaufswiederherstellung erhalten. Vor dem Anwenden wird der
                aktuelle Verlauf gesichert.
            </p>
            <fieldset disabled={pending} className="space-y-3">
                <div className="flex flex-wrap gap-2">
                    <Button
                        variant="ghost"
                        onClick={() =>
                            run(async () => {
                                await tauriInvoke("open_music_state_folder", {
                                    backups: true,
                                });
                            })
                        }
                    >
                        Sicherungsordner öffnen
                    </Button>
                    <Button
                        variant="ghost"
                        onClick={() =>
                            run(async () => {
                                await tauriInvoke("open_music_state_folder", {
                                    backups: false,
                                });
                            })
                        }
                    >
                        Verlaufsordner öffnen
                    </Button>
                </div>
                <Button
                    onClick={() =>
                        run(async () => {
                            await musicStateAction({ action: "backup" });
                        })
                    }
                >
                    Verlauf sichern
                </Button>
                {!data.backups.length && <p>Keine Verlaufssicherungen.</p>}
                <div className="max-h-64 overflow-auto space-y-2">
                    {data.backups.map((backup) => (
                        <div
                            key={backup.id}
                            className="rounded border border-border p-3 space-y-2"
                        >
                            <p className="break-all">{backup.id}</p>
                            <small>
                                {new Date(backup.at).toLocaleString()} ·{" "}
                                {backup.bytes} Bytes
                            </small>
                            <div className="flex gap-2">
                                <Button
                                    aria-label={`Sicherung prüfen: ${backup.id}`}
                                    onClick={() =>
                                        run(async () => {
                                            const value =
                                                await musicStateAction<BackupPreview>(
                                                    {
                                                        action: "backup_preview",
                                                        id: backup.id,
                                                    },
                                                );
                                            setPreview({
                                                id: backup.id,
                                                value,
                                            });
                                        })
                                    }
                                >
                                    Prüfen
                                </Button>
                                <Button
                                    variant="danger"
                                    aria-label={`Sicherung löschen: ${backup.id}`}
                                    onClick={() =>
                                        setDeleting({
                                            kind: "backup",
                                            name: backup.id,
                                        })
                                    }
                                >
                                    Löschen
                                </Button>
                            </div>
                        </div>
                    ))}
                </div>
                <label className="block">
                    Wiederherstellungsprofil
                    <select
                        className={selectClass}
                        value={editing?.Name ?? ""}
                        onChange={(e) => {
                            const profile = data.profiles.find(
                                (p) => p.Name === e.target.value,
                            );
                            if (!profile) {
                                setEditing(null);
                                setProfileName("");
                                return;
                            }
                            setEditing(profile);
                            setOptions(profileOptions(profile));
                            setProfileName(
                                profile.IsBuiltIn ? "" : profile.Name,
                            );
                        }}
                    >
                        <option value="">Eigene Auswahl</option>
                        {data.profiles.map((p) => (
                            <option key={p.Name} value={p.Name}>
                                {p.Name}
                                {p.IsBuiltIn ? " (integriert)" : ""}
                            </option>
                        ))}
                    </select>
                </label>
                <div className="grid gap-2 sm:grid-cols-2">
                    {(
                        Object.keys(defaultRestore) as Array<
                            keyof RestoreOptions
                        >
                    ).map((key) => (
                        <label key={key} className="flex gap-2 items-center">
                            <input
                                type="checkbox"
                                checked={options[key]}
                                onChange={(e) =>
                                    setOptions({
                                        ...options,
                                        [key]: e.target.checked,
                                    })
                                }
                            />
                            {optionLabels[key]}
                        </label>
                    ))}
                </div>
                {!valid && (
                    <p role="alert">
                        Mindestens einen Bereich zur Wiederherstellung
                        auswählen.
                    </p>
                )}
                {preview && (
                    <div
                        role="group"
                        aria-label="Sicherungsvorschau"
                        className="rounded border border-border p-3 space-y-2"
                    >
                        <strong className="break-all">{preview.id}</strong>
                        <p>
                            {preview.value.added.length} hinzugefügt ·{" "}
                            {preview.value.removed.length} entfernt ·{" "}
                            {preview.value.unchanged} unverändert
                        </p>
                        <p className="text-sm text-text-secondary">
                            Die Unterschiede beziehen sich auf den vollständigen
                            Verlauf. Zusammenführen behält aktuelle Einträge;
                            nach dem Anwenden bleiben höchstens 100 Einträge.
                        </p>
                        <details>
                            <summary>
                                Unterschiede und Sicherungsinhalt anzeigen
                            </summary>
                            <p>Hinzugefügt:</p>
                            <ul>
                                {preview.value.added.map((entry) => (
                                    <li key={entry}>{entry}</li>
                                ))}
                            </ul>
                            <p>Entfernt:</p>
                            <ul>
                                {preview.value.removed.map((entry) => (
                                    <li key={entry}>{entry}</li>
                                ))}
                            </ul>
                            <p>Verlauf der Sicherung:</p>
                            <ul>
                                {preview.value.backup.Entries.map(
                                    (entry, index) => (
                                        <li key={`${entry}-${index}`}>
                                            {entry}
                                        </li>
                                    ),
                                )}
                            </ul>
                            <pre className="max-h-48 overflow-auto text-xs whitespace-pre-wrap">
                                {JSON.stringify(preview.value.backup, null, 2)}
                            </pre>
                        </details>
                        <Button
                            disabled={!valid}
                            onClick={() =>
                                run(async () => {
                                    await musicStateAction({
                                        action: "backup_restore",
                                        id: preview.id,
                                        options,
                                        original: preview.value.original,
                                    });
                                    setPreview(null);
                                })
                            }
                        >
                            Geprüfte Sicherung anwenden
                        </Button>
                        <Button
                            variant="ghost"
                            onClick={() => setPreview(null)}
                        >
                            Vorschau schließen
                        </Button>
                    </div>
                )}
                <label className="block">
                    Eigener Profilname
                    <Input
                        value={profileName}
                        maxLength={160}
                        onChange={(e) => setProfileName(e.target.value)}
                    />
                </label>
                <div className="flex flex-wrap gap-2">
                    <Button
                        disabled={
                            !profileName.trim() ||
                            !valid ||
                            data.profiles.some(
                                (p) =>
                                    p.IsBuiltIn &&
                                    p.Name.toLowerCase() ===
                                        profileName.trim().toLowerCase(),
                            )
                        }
                        onClick={() =>
                            run(async () => {
                                const existing = data.profiles.find(
                                    (p) =>
                                        !p.IsBuiltIn &&
                                        p.Name.toLowerCase() ===
                                            profileName.trim().toLowerCase(),
                                );
                                await musicStateAction({
                                    action: "profile_save",
                                    profile: {
                                        ...existing,
                                        Name: profileName.trim(),
                                        ...options,
                                        IsBuiltIn: false,
                                    },
                                });
                                setProfileName("");
                                setEditing(null);
                            })
                        }
                    >
                        Wiederherstellungsprofil speichern
                    </Button>
                    <Button
                        variant="danger"
                        disabled={!editing || editing.IsBuiltIn}
                        onClick={() => {
                            if (editing)
                                setDeleting({
                                    kind: "profile",
                                    name: editing.Name,
                                });
                        }}
                    >
                        Gewähltes Profil löschen
                    </Button>
                    <Button
                        variant="ghost"
                        onClick={() =>
                            run(async () => {
                                const path = await save({
                                    defaultPath:
                                        "spotify-history-profiles.json",
                                    filters: [
                                        {
                                            name: "Spotify-Profile",
                                            extensions: ["json"],
                                        },
                                    ],
                                });
                                if (path)
                                    await musicStateAction({
                                        action: "profiles_export",
                                        path,
                                    });
                            })
                        }
                    >
                        Profile exportieren
                    </Button>
                    <Button
                        variant="ghost"
                        onClick={() =>
                            run(async () => {
                                const path = await open({
                                    multiple: false,
                                    filters: [
                                        {
                                            name: "Spotify-Profile",
                                            extensions: ["json"],
                                        },
                                    ],
                                });
                                if (typeof path !== "string") return;
                                const value =
                                    await musicStateAction<ProfilePreview>({
                                        action: "profiles_preview",
                                        path,
                                    });
                                setProfileReview(value);
                                setImportActions(
                                    value.profiles.map((row) =>
                                        row.status === "new"
                                            ? "overwrite"
                                            : "skip",
                                    ),
                                );
                            })
                        }
                    >
                        Profile importieren
                    </Button>
                </div>
                {profileReview && (
                    <div
                        role="group"
                        aria-label="Profilimport prüfen"
                        className="rounded border border-border p-3 space-y-2"
                    >
                        {profileReview.profiles.map((row, i) => (
                            <div key={`${row.profile.Name}-${i}`}>
                                <p>
                                    {row.profile.Name} ·{" "}
                                    {(
                                        {
                                            new: "neu",
                                            changed: "geändert",
                                            unchanged: "unverändert",
                                        } as Record<string, string>
                                    )[row.status] ?? row.status}
                                </p>
                                <details>
                                    <summary>Profiloptionen anzeigen</summary>
                                    <pre className="whitespace-pre-wrap text-xs">
                                        {JSON.stringify(row.profile, null, 2)}
                                    </pre>
                                </details>
                                <label>
                                    Importaktion: {row.profile.Name}
                                    <select
                                        className={selectClass}
                                        value={importActions[i]}
                                        onChange={(e) =>
                                            setImportActions(
                                                importActions.map((value, n) =>
                                                    n === i
                                                        ? e.target.value
                                                        : value,
                                                ),
                                            )
                                        }
                                    >
                                        <option value="overwrite">
                                            {row.status === "new"
                                                ? "Übernehmen"
                                                : "Überschreiben"}
                                        </option>
                                        <option value="copy">
                                            Als Kopie übernehmen
                                        </option>
                                        <option value="skip">
                                            Überspringen
                                        </option>
                                    </select>
                                </label>
                            </div>
                        ))}
                        <Button
                            disabled={importActions.every(
                                (action) => action === "skip",
                            )}
                            onClick={() =>
                                run(async () => {
                                    await musicStateAction({
                                        action: "profiles_import",
                                        proposals: profileReview.profiles,
                                        actions: importActions,
                                        original: profileReview.original,
                                    });
                                    setProfileReview(null);
                                })
                            }
                        >
                            Geprüfte Profile importieren
                        </Button>
                        <Button
                            variant="ghost"
                            onClick={() => setProfileReview(null)}
                        >
                            Import abbrechen
                        </Button>
                    </div>
                )}
                {deleting && (
                    <div
                        role="group"
                        aria-label="Sicherung oder Profil löschen"
                        className="space-y-2"
                    >
                        <p className="break-all">
                            „{deleting.name}“ dauerhaft löschen?
                        </p>
                        <Button
                            variant="danger"
                            onClick={() =>
                                run(async () => {
                                    await musicStateAction(
                                        deleting.kind === "backup"
                                            ? {
                                                  action: "backup_delete",
                                                  id: deleting.name,
                                              }
                                            : {
                                                  action: "profile_delete",
                                                  name: deleting.name,
                                              },
                                    );
                                    if (preview?.id === deleting.name)
                                        setPreview(null);
                                    if (editing?.Name === deleting.name) {
                                        setEditing(null);
                                        setProfileName("");
                                    }
                                    setDeleting(null);
                                })
                            }
                        >
                            Löschen bestätigen
                        </Button>
                        <Button
                            variant="ghost"
                            onClick={() => setDeleting(null)}
                        >
                            Löschen abbrechen
                        </Button>
                    </div>
                )}
            </fieldset>
        </Card>
    );
}
