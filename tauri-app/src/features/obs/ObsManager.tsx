import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { useObsQuery, type Control, type Item } from "./management-api";
import { SourceControls, NumberSetting } from "./SourceControls";
const selectClass = "rounded border border-border bg-panel px-2 py-1";
export function ObsManager({ enabled }: { enabled: boolean }) {
    return (
        <Card className="space-y-4">
            <h2 className="text-lg font-semibold">OBS-Verwaltung</h2>
            {enabled ? <ConnectedManager /> : <p>OBS nicht verbunden</p>}
        </Card>
    );
}
function ConnectedManager() {
    const client = useQueryClient();
    const [scene, setScene] = useState("");
    const [group, setGroup] = useState<string[]>([]);
    const [selected, setSelected] = useState<number | null>(null);
    const [input, setInput] = useState("");
    const profiles = useObsQuery<{
        currentProfileName: string;
        profiles: { profileName: string }[];
    }>({ query: "profiles" });
    const collections = useObsQuery<{
        currentSceneCollectionName: string;
        sceneCollections: { sceneCollectionName: string }[];
    }>({ query: "scene_collections" });
    const transitions = useObsQuery<{
        transitions: { transitionName: string }[];
    }>({ query: "transitions" });
    const current = useObsQuery<{
        transitionName: string;
        transitionDuration: number;
        transitionFixed: boolean;
    }>({ query: "current_transition" });
    const scenes = useQuery({
        queryKey: ["obs-management", "scenes"],
        queryFn: () =>
            tauriInvoke<{ name: string; index: number }[]>("obs_scenes"),
        retry: false,
    });
    const inputs = useObsQuery<{ inputs: { inputName: string }[] }>({
        query: "inputs",
    });
    const target = group[group.length - 1] ?? scene;
    const items = useObsQuery<{ sceneItems: Item[] }>(
        {
            query: group.length ? "group_items" : "scene_items",
            sceneName: target,
        },
        Boolean(target),
    );
    const mutation = useMutation({
        mutationFn: (control: Control) =>
            tauriInvoke("obs_control", { control }),
        onSuccess: async (_, control) => {
            if (control.action === "set_scene_collection") {
                setScene("");
                setGroup([]);
                setSelected(null);
                setInput("");
            }
            await client.invalidateQueries({ queryKey: ["obs-management"] });
        },
    });
    const apply = (control: Control) => mutation.mutateAsync(control);
    const item = items.data?.sceneItems?.find(
        (i) => i.sceneItemId === selected,
    );
    const errors = [
        profiles.error,
        collections.error,
        transitions.error,
        current.error,
        scenes.error,
        inputs.error,
        items.error,
        mutation.error,
    ].filter(Boolean);
    return (
        <>
            <fieldset disabled={mutation.isPending} className="space-y-4">
                <div className="flex flex-wrap gap-4">
                    <label>
                        OBS-Profil{" "}
                        <select
                            className={selectClass}
                            disabled={!profiles.data}
                            value={profiles.data?.currentProfileName ?? ""}
                            onChange={(e) =>
                                mutation.mutate({
                                    action: "set_profile",
                                    profileName: e.target.value,
                                })
                            }
                        >
                            <option value="" disabled>
                                Profil auswählen
                            </option>
                            {profiles.data?.profiles?.map((p) => (
                                <option key={p.profileName}>
                                    {p.profileName}
                                </option>
                            ))}
                        </select>
                    </label>
                    <label>
                        Szenensammlung{" "}
                        <select
                            className={selectClass}
                            disabled={!collections.data}
                            value={
                                collections.data?.currentSceneCollectionName ??
                                ""
                            }
                            onChange={(e) =>
                                mutation.mutate({
                                    action: "set_scene_collection",
                                    sceneCollectionName: e.target.value,
                                })
                            }
                        >
                            <option value="" disabled>
                                Sammlung auswählen
                            </option>
                            {collections.data?.sceneCollections?.map((p) => (
                                <option key={p.sceneCollectionName}>
                                    {p.sceneCollectionName}
                                </option>
                            ))}
                        </select>
                    </label>
                    <label>
                        Übergang{" "}
                        <select
                            className={selectClass}
                            disabled={!transitions.data || !current.data}
                            value={current.data?.transitionName ?? ""}
                            onChange={(e) =>
                                mutation.mutate({
                                    action: "set_transition",
                                    transitionName: e.target.value,
                                })
                            }
                        >
                            <option value="" disabled>
                                Übergang auswählen
                            </option>
                            {transitions.data?.transitions?.map((p) => (
                                <option key={p.transitionName}>
                                    {p.transitionName}
                                </option>
                            ))}
                        </select>
                    </label>
                    <NumberSetting
                        label="Übergangsdauer (ms)"
                        value={current.data?.transitionDuration}
                        disabled={current.data?.transitionFixed}
                        min={50}
                        max={20000}
                        apply={(n) =>
                            apply({
                                action: "set_transition_duration",
                                transitionDuration: n,
                            })
                        }
                    />
                </div>
                <div className="flex flex-wrap gap-3">
                    <label>
                        Szene verwalten{" "}
                        <select
                            className={selectClass}
                            value={scene}
                            onChange={(e) => {
                                setScene(e.target.value);
                                setGroup([]);
                                setSelected(null);
                                setInput("");
                                mutation.reset();
                            }}
                        >
                            <option value="">Szene auswählen</option>
                            {scenes.data?.map((s) => (
                                <option key={s.name}>{s.name}</option>
                            ))}
                        </select>
                    </label>
                    <label>
                        Audio-/Eingangsquelle{" "}
                        <select
                            className={selectClass}
                            value={input}
                            onChange={(e) => {
                                setInput(e.target.value);
                                setSelected(null);
                                mutation.reset();
                            }}
                        >
                            <option value="">Quelle auswählen</option>
                            {inputs.data?.inputs?.map((i) => (
                                <option key={i.inputName}>{i.inputName}</option>
                            ))}
                        </select>
                    </label>
                </div>
                {group.length > 0 && (
                    <Button
                        variant="ghost"
                        onClick={() => {
                            setGroup(group.slice(0, -1));
                            setSelected(null);
                            setInput("");
                        }}
                    >
                        Zurück aus {target}
                    </Button>
                )}
                {target && (
                    <ul className="space-y-1">
                        {[...(items.data?.sceneItems ?? [])]
                            .sort((a, b) => b.sceneItemIndex - a.sceneItemIndex)
                            .map((i) => (
                                <li
                                    key={i.sceneItemId}
                                    className="flex items-center gap-2"
                                >
                                    <Button
                                        variant={
                                            selected === i.sceneItemId
                                                ? "primary"
                                                : "ghost"
                                        }
                                        onClick={() => {
                                            setSelected(i.sceneItemId);
                                            setInput(i.sourceName);
                                            mutation.reset();
                                        }}
                                    >
                                        {i.sourceName} bearbeiten
                                    </Button>
                                    <span className="text-sm text-text-secondary">
                                        {i.sceneItemEnabled
                                            ? "Sichtbar"
                                            : "Ausgeblendet"}
                                        {i.sceneItemLocked ? " · Gesperrt" : ""}
                                    </span>
                                    {i.isGroup && (
                                        <Button
                                            variant="ghost"
                                            onClick={() => {
                                                setGroup([
                                                    ...group,
                                                    i.sourceName,
                                                ]);
                                                setSelected(null);
                                                setInput("");
                                            }}
                                        >
                                            Gruppe öffnen
                                        </Button>
                                    )}
                                </li>
                            ))}
                    </ul>
                )}
                {input && (
                    <SourceControls
                        key={`${target}:${selected}:${input}`}
                        input={input}
                        scene={target}
                        item={item}
                        itemCount={items.data?.sceneItems?.length ?? 0}
                        apply={apply}
                    />
                )}
            </fieldset>
            <Button
                variant="ghost"
                onClick={() =>
                    void client.invalidateQueries({
                        queryKey: ["obs-management"],
                    })
                }
            >
                OBS-Verwaltung aktualisieren
            </Button>
            {errors.map((error, index) => (
                <p key={index} role="alert">
                    {String(error)}
                </p>
            ))}
        </>
    );
}
