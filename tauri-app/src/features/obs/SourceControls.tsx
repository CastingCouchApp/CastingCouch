import { useEffect, useState } from "react";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { useObsQuery, type Item, type Apply } from "./management-api";
export function NumberSetting({
    label,
    value,
    apply,
    disabled = false,
    min,
    max,
    step = 1,
}: {
    label: string;
    value?: number;
    apply: (n: number) => Promise<unknown>;
    disabled?: boolean;
    min?: number;
    max?: number;
    step?: number;
}) {
    const [draft, setDraft] = useState("");
    const [dirty, setDirty] = useState(false);
    const [pending, setPending] = useState(false);
    const [error, setError] = useState("");
    useEffect(() => {
        if (!dirty) setDraft(Number.isFinite(value) ? String(value) : "");
    }, [value, dirty]);
    const valid =
        draft.trim() !== "" &&
        Number.isFinite(Number(draft)) &&
        (min === undefined || Number(draft) >= min) &&
        (max === undefined || Number(draft) <= max);
    return (
        <form
            className="flex flex-wrap items-end gap-2"
            onSubmit={async (e) => {
                e.preventDefault();
                if (!valid) return;
                setPending(true);
                setError("");
                try {
                    await apply(Number(draft));
                    setDirty(false);
                } catch (e) {
                    setError(String(e));
                } finally {
                    setPending(false);
                }
            }}
        >
            <label>
                {label}
                <Input
                    type="number"
                    value={draft}
                    min={min}
                    max={max}
                    step={step}
                    disabled={disabled || !Number.isFinite(value) || pending}
                    onChange={(e) => {
                        setDraft(e.target.value);
                        setDirty(true);
                    }}
                />
            </label>
            <Button
                type="submit"
                disabled={
                    disabled ||
                    !Number.isFinite(value) ||
                    !dirty ||
                    !valid ||
                    pending
                }
                aria-label={`${label} übernehmen`}
            >
                Übernehmen
            </Button>
            {error && <p role="alert">{error}</p>}
        </form>
    );
}
export function SourceControls({
    input,
    scene,
    item,
    itemCount,
    apply,
}: {
    input: string;
    scene: string;
    item?: Item;
    itemCount: number;
    apply: Apply;
}) {
    const transform = useObsQuery<{
        sceneItemTransform: Record<string, number>;
    }>(
        {
            query: "transform",
            sceneName: scene,
            sceneItemId: item?.sceneItemId ?? 0,
        },
        Boolean(item),
    );
    const mute = useObsQuery<{ inputMuted: boolean }>({
        query: "mute",
        inputName: input,
    });
    const volume = useObsQuery<{ inputVolumeDb: number }>({
        query: "volume",
        inputName: input,
    });
    const monitor = useObsQuery<{ monitorType: string }>({
        query: "audio_monitor",
        inputName: input,
    });
    const sync = useObsQuery<{ inputAudioSyncOffset: number }>({
        query: "audio_sync_offset",
        inputName: input,
    });
    const filters = useObsQuery<{
        filters: { filterName: string; filterEnabled: boolean }[];
    }>({ query: "filters", sourceName: input });
    const settings = useObsQuery<{ inputSettings: Record<string, unknown> }>({
        query: "input_settings",
        inputName: input,
    });
    const [error, setError] = useState("");
    const act = (control: Parameters<Apply>[0]) => {
        setError("");
        void apply(control).catch((e) => setError(String(e)));
    };
    const transformLabels: Record<string, string> = {
        positionX: "Position X",
        positionY: "Position Y",
        scaleX: "Skalierung X",
        scaleY: "Skalierung Y",
        rotation: "Drehung",
        cropLeft: "Zuschnitt links",
        cropRight: "Zuschnitt rechts",
        cropTop: "Zuschnitt oben",
        cropBottom: "Zuschnitt unten",
        boundsWidth: "Begrenzungsbreite",
        boundsHeight: "Begrenzungshöhe",
    };
    return (
        <section className="space-y-4 rounded border border-border p-3">
            <h3 className="font-semibold">{input}</h3>
            {item && (
                <>
                    <div className="flex flex-wrap gap-4">
                        <label>
                            <input
                                type="checkbox"
                                checked={item.sceneItemEnabled}
                                onChange={(e) =>
                                    act({
                                        action: "set_visibility",
                                        sceneName: scene,
                                        sceneItemId: item.sceneItemId,
                                        sceneItemEnabled: e.target.checked,
                                    })
                                }
                            />{" "}
                            Quelle sichtbar
                        </label>
                        <label>
                            <input
                                type="checkbox"
                                checked={item.sceneItemLocked}
                                onChange={(e) =>
                                    act({
                                        action: "set_locked",
                                        sceneName: scene,
                                        sceneItemId: item.sceneItemId,
                                        sceneItemLocked: e.target.checked,
                                    })
                                }
                            />{" "}
                            Quelle gesperrt
                        </label>
                        <Button
                            disabled={
                                item.sceneItemLocked ||
                                item.sceneItemIndex >= itemCount - 1
                            }
                            onClick={() =>
                                act({
                                    action: "set_index",
                                    sceneName: scene,
                                    sceneItemId: item.sceneItemId,
                                    sceneItemIndex: item.sceneItemIndex + 1,
                                })
                            }
                        >
                            Nach vorne
                        </Button>
                        <Button
                            disabled={
                                item.sceneItemLocked || item.sceneItemIndex <= 0
                            }
                            onClick={() =>
                                act({
                                    action: "set_index",
                                    sceneName: scene,
                                    sceneItemId: item.sceneItemId,
                                    sceneItemIndex: item.sceneItemIndex - 1,
                                })
                            }
                        >
                            Nach hinten
                        </Button>
                    </div>
                    <details>
                        <summary className="cursor-pointer">
                            Position, Skalierung und Zuschnitt
                        </summary>
                        <div className="grid gap-3 md:grid-cols-2">
                            {Object.entries(transformLabels).map(
                                ([field, label]) => (
                                    <NumberSetting
                                        key={field}
                                        label={label}
                                        value={
                                            transform.data
                                                ?.sceneItemTransform?.[field]
                                        }
                                        step={
                                            field.startsWith("scale") ? 0.01 : 1
                                        }
                                        disabled={
                                            item.sceneItemLocked ||
                                            transform.isError
                                        }
                                        min={
                                            field.startsWith("crop") ||
                                            field.startsWith("bounds")
                                                ? 0
                                                : undefined
                                        }
                                        apply={(n) =>
                                            apply({
                                                action: "set_transform",
                                                sceneName: scene,
                                                sceneItemId: item.sceneItemId,
                                                sceneItemTransform: {
                                                    [field]: n,
                                                },
                                            })
                                        }
                                    />
                                ),
                            )}
                        </div>
                    </details>
                </>
            )}
            <h4 className="font-medium">Audio</h4>
            <div className="flex flex-wrap items-center gap-4">
                <label>
                    <input
                        type="checkbox"
                        disabled={
                            typeof mute.data?.inputMuted !== "boolean" ||
                            mute.isError
                        }
                        checked={mute.data?.inputMuted ?? false}
                        onChange={(e) =>
                            act({
                                action: "set_mute",
                                inputName: input,
                                inputMuted: e.target.checked,
                            })
                        }
                    />{" "}
                    Stumm
                </label>
                <NumberSetting
                    label="Lautstärke (dB)"
                    value={volume.data?.inputVolumeDb}
                    disabled={volume.isError}
                    min={-100}
                    max={26}
                    step={0.1}
                    apply={(n) =>
                        apply({
                            action: "set_volume",
                            inputName: input,
                            inputVolumeDb: n,
                        })
                    }
                />
                <label>
                    Audio-Monitoring{" "}
                    <select
                        className="rounded border border-border bg-panel p-2"
                        disabled={!monitor.data || monitor.isError}
                        value={monitor.data?.monitorType ?? ""}
                        onChange={(e) =>
                            act({
                                action: "set_monitor",
                                inputName: input,
                                monitorType: e.target.value,
                            })
                        }
                    >
                        <option value="" disabled>
                            Nicht verfügbar
                        </option>
                        <option value="OBS_MONITORING_TYPE_NONE">Aus</option>
                        <option value="OBS_MONITORING_TYPE_MONITOR_ONLY">
                            Nur Monitoring
                        </option>
                        <option value="OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT">
                            Monitoring und Ausgabe
                        </option>
                    </select>
                </label>
                <NumberSetting
                    label="Synchronisationsversatz (ms)"
                    value={sync.data?.inputAudioSyncOffset}
                    disabled={sync.isError}
                    min={-950}
                    max={20000}
                    apply={(n) =>
                        apply({
                            action: "set_sync_offset",
                            inputName: input,
                            inputAudioSyncOffset: n,
                        })
                    }
                />
            </div>
            {[mute.error, volume.error, monitor.error, sync.error].filter(
                Boolean,
            ).length > 0 && (
                <p className="text-sm text-text-secondary">
                    Nicht verfügbare Audioparameter sind deaktiviert:{" "}
                    {[mute.error, volume.error, monitor.error, sync.error]
                        .filter(Boolean)
                        .map(String)
                        .join("; ")}
                </p>
            )}
            <h4 className="font-medium">Filter</h4>
            <div className="flex flex-wrap gap-4">
                {filters.data?.filters?.map((f) => (
                    <label key={f.filterName}>
                        <input
                            type="checkbox"
                            checked={f.filterEnabled}
                            onChange={(e) =>
                                act({
                                    action: "set_filter",
                                    sourceName: input,
                                    filterName: f.filterName,
                                    filterEnabled: e.target.checked,
                                })
                            }
                        />{" "}
                        {f.filterName}
                    </label>
                ))}
            </div>
            {settings.data && (
                <InputSettings
                    key={JSON.stringify(settings.data.inputSettings)}
                    values={settings.data.inputSettings}
                    input={input}
                    apply={apply}
                />
            )}
            {[filters.error, settings.error, transform.error, error]
                .filter(Boolean)
                .map((e, i) => (
                    <p key={i} role="alert">
                        {String(e)}
                    </p>
                ))}
        </section>
    );
}
function InputSettings({
    values,
    input,
    apply,
}: {
    values: Record<string, unknown>;
    input: string;
    apply: Apply;
}) {
    const [draft, setDraft] = useState<Record<string, unknown>>({});
    const [error, setError] = useState("");
    const [pending, setPending] = useState(false);
    const labels: Record<string, string> = {
        url: "Browser-URL",
        text: "Quellentext",
        local_file: "Mediendatei",
        width: "Quellenbreite",
        height: "Quellenhöhe",
        looping: "Medien wiederholen",
        restart_on_activate: "Bei Aktivierung neu starten",
        shutdown: "Browser bei Unsichtbarkeit beenden",
        device_id: "Audiogerät-ID",
        video_device_id: "Videogerät-ID",
    };
    const fields = Object.entries(labels).filter(([key]) =>
        ["string", "number", "boolean"].includes(typeof values[key]),
    );
    if (!fields.length) return null;
    return (
        <details>
            <summary className="cursor-pointer">Quelleneinstellungen</summary>
            <form
                className="space-y-3 pt-3"
                onSubmit={async (e) => {
                    e.preventDefault();
                    setPending(true);
                    setError("");
                    try {
                        await apply({
                            action: "set_input_settings",
                            inputName: input,
                            inputSettings: draft,
                        });
                    } catch (e) {
                        setError(String(e));
                    } finally {
                        setPending(false);
                    }
                }}
            >
                {fields.map(([key, label]) => {
                    const value = draft[key] ?? values[key];
                    return (
                        <label className="block" key={key}>
                            {label}
                            {typeof value === "boolean" ? (
                                <input
                                    type="checkbox"
                                    checked={value}
                                    onChange={(e) =>
                                        setDraft({
                                            ...draft,
                                            [key]: e.target.checked,
                                        })
                                    }
                                />
                            ) : (
                                <Input
                                    type={
                                        typeof values[key] === "number"
                                            ? "number"
                                            : "text"
                                    }
                                    value={String(value)}
                                    onChange={(e) =>
                                        setDraft({
                                            ...draft,
                                            [key]:
                                                typeof values[key] === "number"
                                                    ? Number(e.target.value)
                                                    : e.target.value,
                                        })
                                    }
                                />
                            )}
                        </label>
                    );
                })}
                <Button
                    type="submit"
                    disabled={pending || !Object.keys(draft).length}
                >
                    Quelleneinstellungen speichern
                </Button>
                {error && <p role="alert">{error}</p>}
            </form>
        </details>
    );
}
