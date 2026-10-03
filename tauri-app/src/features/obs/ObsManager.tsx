import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { useObsQuery, type Control, type Item } from "./management-api";
import { SourceControls, NumberSetting } from "./SourceControls";
const selectClass = "rounded border border-border bg-panel px-2 py-1";
const contains = (value: string | undefined, search: string) =>
    (value ?? "").toLowerCase().includes(search.trim().toLowerCase());
const sameName = (a: string, b: string | null | undefined) =>
    a.toLowerCase() === b?.toLowerCase();
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
    const [selected, setSelected] = useState<{
        id: number;
        name: string;
    } | null>(null);
    const [input, setInput] = useState("");
    const [globalInput, setGlobalInput] = useState(true);
    const [sceneSearch, setSceneSearch] = useState("");
    const [sourceSearch, setSourceSearch] = useState("");
    const [inputSearch, setInputSearch] = useState("");
    const [inputMode, setInputMode] = useState("all");
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
    const currentScene = useQuery({
        queryKey: ["obs-management", "current-scene"],
        queryFn: () => tauriInvoke<string | null>("obs_current_scene"),
        refetchInterval: 3000,
        retry: false,
    });
    const inputs = useObsQuery<{
        inputs: {
            inputName: string;
            inputKind?: string;
            unversionedInputKind?: string;
            category: string;
            inputMuted?: boolean | null;
            muteError?: string | null;
        }[];
    }>({ query: "input_catalog" }, true, 5000);
    const currentSceneName = currentScene.isError ? null : currentScene.data;
    const visibleScenes = useMemo(
        () =>
            (scenes.isError ? [] : (scenes.data ?? []))
                .filter((s) => contains(s.name, sceneSearch))
                .sort(
                    (a, b) =>
                        Number(sameName(b.name, currentSceneName)) -
                            Number(sameName(a.name, currentSceneName)) ||
                        a.index - b.index,
                ),
        [scenes.data, scenes.isError, currentSceneName, sceneSearch],
    );
    const effectiveScene =
        visibleScenes.find((s) => sameName(s.name, scene))?.name ??
        visibleScenes.find((s) => sameName(s.name, currentSceneName))?.name ??
        "";
    const activeGroup = effectiveScene === scene ? group : [];
    const target = activeGroup[activeGroup.length - 1] ?? effectiveScene;
    const items = useObsQuery<{ sceneItems: Item[] }>(
        {
            query: activeGroup.length ? "group_items" : "scene_items",
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
    const visibleInputs = useMemo(
        () =>
            (inputs.isError ? [] : (inputs.data?.inputs ?? []))
                .filter(
                    (i) =>
                        contains(i.inputName, inputSearch) ||
                        contains(i.inputKind, inputSearch),
                )
                .filter(
                    (i) =>
                        inputMode === "all" ||
                        (inputMode === "muted"
                            ? i.inputMuted === true
                            : i.category === inputMode),
                )
                .sort(
                    (a, b) =>
                        a.category.localeCompare(b.category) ||
                        a.inputName
                            .toLowerCase()
                            .localeCompare(b.inputName.toLowerCase()),
                ),
        [inputs.data, inputs.isError, inputSearch, inputMode],
    );
    const visibleItems = useMemo(
        () =>
            (items.isError || !target ? [] : (items.data?.sceneItems ?? []))
                .filter(
                    (i) =>
                        contains(i.sourceName, sourceSearch) ||
                        contains(i.sourceType, sourceSearch) ||
                        contains(i.inputKind, sourceSearch),
                )
                .sort(
                    (a, b) =>
                        b.sceneItemIndex - a.sceneItemIndex ||
                        a.sourceName.localeCompare(b.sourceName),
                ),
        [items.data, items.isError, target, sourceSearch],
    );
    const item =
        effectiveScene === scene
            ? visibleItems.find(
                  (i) =>
                      i.sceneItemId === selected?.id &&
                      sameName(i.sourceName, selected?.name),
              )
            : undefined;
    const effectiveInput = inputs.isError
        ? ""
        : globalInput
          ? (visibleInputs.find((i) => sameName(i.inputName, input))
                ?.inputName ??
            visibleInputs[0]?.inputName ??
            "")
          : (item?.sourceName ?? "");
    useEffect(() => {
        if (scene !== effectiveScene) {
            setScene(effectiveScene);
            setGroup([]);
            setSelected(null);
            if (!globalInput) setInput("");
        }
    }, [scene, effectiveScene, globalInput]);
    useEffect(() => {
        if (selected && !item) {
            setSelected(null);
            setInput("");
        }
    }, [selected, item]);
    useEffect(() => {
        if (globalInput && input !== effectiveInput) setInput(effectiveInput);
    }, [globalInput, input, effectiveInput]);
    const errors = [
        profiles.error,
        collections.error,
        transitions.error,
        current.error,
        scenes.error,
        currentScene.error,
        inputs.error,
        items.error,
        ["restart_media", "stop_media", "refresh_browser"].includes(
            mutation.variables?.action ?? "",
        )
            ? null
            : mutation.error,
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
                        Szenen suchen
                        <Input
                            value={sceneSearch}
                            onChange={(e) => setSceneSearch(e.target.value)}
                        />
                    </label>
                    <label>
                        Szene verwalten{" "}
                        <select
                            className={selectClass}
                            value={effectiveScene}
                            disabled={scenes.isError || !scenes.data}
                            onChange={(e) => {
                                setScene(e.target.value);
                                setGroup([]);
                                setSelected(null);
                                setInput("");
                                mutation.reset();
                            }}
                        >
                            <option value="">Szene auswählen</option>
                            {visibleScenes.map((s) => (
                                <option key={s.name}>{s.name}</option>
                            ))}
                        </select>
                    </label>
                    <label>
                        Eingänge suchen
                        <Input
                            value={inputSearch}
                            onChange={(e) => {
                                setInputSearch(e.target.value);
                                setGlobalInput(true);
                                setSelected(null);
                            }}
                        />
                    </label>
                    <label>
                        Eingänge filtern
                        <select
                            className={selectClass}
                            value={inputMode}
                            onChange={(e) => {
                                setInputMode(e.target.value);
                                setGlobalInput(true);
                                setSelected(null);
                            }}
                        >
                            <option value="all">Alle Eingänge</option>
                            <option value="microphone">Nur Mikrofone</option>
                            <option value="game">Nur Spiel/Desktop</option>
                            <option value="music">Nur Musik</option>
                            <option value="browser">Nur Browser/Alerts</option>
                            <option value="muted">Nur stumme Eingänge</option>
                        </select>
                    </label>
                    <label>
                        Audio-/Eingangsquelle{" "}
                        <select
                            className={selectClass}
                            value={globalInput ? effectiveInput : ""}
                            disabled={inputs.isError || !inputs.data}
                            onChange={(e) => {
                                setInput(e.target.value);
                                setGlobalInput(true);
                                setSelected(null);
                                mutation.reset();
                            }}
                        >
                            <option value="">Quelle auswählen</option>
                            {visibleInputs.map((i) => (
                                <option key={i.inputName}>{i.inputName}</option>
                            ))}
                        </select>
                    </label>
                </div>
                {inputMode === "muted" &&
                    !inputs.isError &&
                    inputs.data?.inputs
                        ?.filter((i) => i.muteError)
                        .map((i) => (
                            <p key={i.inputName} role="alert">
                                {i.inputName}: {i.muteError}
                            </p>
                        ))}
                <label className="block">
                    Quellen suchen
                    <Input
                        value={sourceSearch}
                        onChange={(e) => setSourceSearch(e.target.value)}
                    />
                </label>
                {activeGroup.length > 0 && (
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
                        {visibleItems.map((i) => (
                            <li
                                key={i.sceneItemId}
                                className="flex items-center gap-2"
                            >
                                <Button
                                    variant={
                                        selected?.id === i.sceneItemId &&
                                        selected.name === i.sourceName
                                            ? "primary"
                                            : "ghost"
                                    }
                                    onClick={() => {
                                        setSelected({
                                            id: i.sceneItemId,
                                            name: i.sourceName,
                                        });
                                        setInput(i.sourceName);
                                        setGlobalInput(false);
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
                                            setGroup([...group, i.sourceName]);
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
                {effectiveInput && (
                    <SourceControls
                        key={`${target}:${selected?.id}:${effectiveInput}`}
                        input={effectiveInput}
                        inputKind={
                            !inputs.isError && !items.isError
                                ? inputs.data?.inputs?.find(
                                      (entry) =>
                                          entry.inputName === effectiveInput,
                                  )?.unversionedInputKind ||
                                  inputs.data?.inputs?.find(
                                      (entry) =>
                                          entry.inputName === effectiveInput,
                                  )?.inputKind
                                : undefined
                        }
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
