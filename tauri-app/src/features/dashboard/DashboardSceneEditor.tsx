import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { open } from "@tauri-apps/plugin-dialog";
import { tauriInvoke, queryKeys, FALLBACK_POLL_MS } from "../../lib/api";
import type { SceneButtonDraft } from "../../lib/command-contract";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { SCENE_GLYPHS, SceneIcon } from "./DashboardSceneButtons";
const selectClass =
    "rounded-md border border-border bg-input px-2 py-1 text-text";
export function DashboardSceneEditor({
    buttons,
    sceneChoices,
    onChange,
}: {
    buttons: SceneButtonDraft[];
    sceneChoices: string[];
    onChange: (
        next: (buttons: SceneButtonDraft[]) => SceneButtonDraft[],
    ) => void;
}) {
    const [error, setError] = useState<string>();
    const scenes = useQuery({
        queryKey: queryKeys.obsScenes,
        queryFn: () => tauriInvoke<{ name: string }[]>("obs_scenes"),
        refetchInterval: FALLBACK_POLL_MS,
        retry: false,
    });
    const assets = useQuery({
        queryKey: ["dashboard-assets"],
        queryFn: () =>
            tauriInvoke<{ id: string; name: string; path: string }[]>(
                "dashboard_asset_choices",
            ),
        retry: false,
    });
    const choices = [
        ...new Set(
            [
                ...sceneChoices,
                ...(scenes.data ?? []).map((s) => s.name),
                ...buttons.map((b) => b.sceneName),
            ].filter(Boolean),
        ),
    ];
    const patch = (id: string, values: Partial<SceneButtonDraft>) =>
        onChange((rows) =>
            rows.map((b) => (b.id === id ? { ...b, ...values } : b)),
        );
    const move = (id: string, delta: number) =>
        onChange((rows) => {
            const next = [...rows],
                i = next.findIndex((b) => b.id === id),
                target = i + delta;
            if (i >= 0 && target >= 0 && target < next.length)
                [next[i], next[target]] = [next[target], next[i]];
            return next;
        });
    return (
        <section className="space-y-3">
            <h3 className="font-semibold">Szenenbuttons</h3>
            <p className="text-sm text-muted">
                Gespeicherte Szenen sind auch ohne OBS-Verbindung auswählbar.
                Bilder bleiben lokale Dateien; Bibliotheksbilder liegen im
                App-Datenordner.
            </p>
            <datalist id="dashboard-scene-choices">
                {choices.map((name) => (
                    <option key={name} value={name} />
                ))}
            </datalist>
            {scenes.isError && (
                <p className="text-sm text-muted">
                    OBS-Szenenliste nicht verfügbar: {String(scenes.error)}.
                    Gespeicherte Auswahl wird verwendet.
                </p>
            )}
            {buttons.map((b, i) => {
                const name = b.title || b.sceneName || `Button ${i + 1}`;
                return (
                    <div
                        key={b.id}
                        className="grid gap-3 rounded border border-border p-3 sm:grid-cols-2"
                    >
                        <div className="flex items-center gap-2 sm:col-span-2">
                            <SceneIcon button={b} />
                            <strong>{name}</strong>
                            <Button
                                variant="ghost"
                                aria-label={`${name} nach oben`}
                                disabled={i === 0}
                                onClick={() => move(b.id, -1)}
                            >
                                ↑
                            </Button>
                            <Button
                                variant="ghost"
                                aria-label={`${name} nach unten`}
                                disabled={i === buttons.length - 1}
                                onClick={() => move(b.id, 1)}
                            >
                                ↓
                            </Button>
                            <Button
                                variant="danger"
                                aria-label={`${name} entfernen`}
                                onClick={() => {
                                    if (
                                        window.confirm(
                                            `Szenenbutton „${name}“ entfernen?`,
                                        )
                                    )
                                        onChange((rows) =>
                                            rows.filter(
                                                (row) => row.id !== b.id,
                                            ),
                                        );
                                }}
                            >
                                Entfernen
                            </Button>
                        </div>
                        <label>
                            Buttonname
                            <Input
                                aria-label={`Buttonname ${name}`}
                                value={b.title}
                                maxLength={200}
                                onChange={(e) =>
                                    patch(b.id, { title: e.target.value })
                                }
                            />
                        </label>
                        <label>
                            OBS-Szene
                            <Input
                                aria-label={`Szene ${name}`}
                                list="dashboard-scene-choices"
                                value={b.sceneName}
                                maxLength={200}
                                onChange={(e) =>
                                    patch(b.id, { sceneName: e.target.value })
                                }
                            />
                        </label>
                        <label>
                            Symboltyp{" "}
                            <select
                                className={selectClass}
                                aria-label={`Symboltyp ${name}`}
                                value={b.iconKind}
                                onChange={(e) =>
                                    patch(b.id, {
                                        iconKind: e.target.value,
                                        iconValue:
                                            e.target.value === "Glyph"
                                                ? "\ue714"
                                                : e.target.value === "Emoji"
                                                  ? "🎬"
                                                  : "",
                                    })
                                }
                            >
                                {!["Emoji", "Glyph", "Image"].includes(
                                    b.iconKind,
                                ) && (
                                    <option value={b.iconKind}>
                                        Alt: {b.iconKind}
                                    </option>
                                )}
                                {["Emoji", "Glyph", "Image"].map((k) => (
                                    <option key={k} value={k}>
                                        {k === "Image"
                                            ? "Bild"
                                            : k === "Glyph"
                                              ? "Symbol"
                                              : k}
                                    </option>
                                ))}
                            </select>
                        </label>
                        <label>
                            Farbe{" "}
                            <Input
                                aria-label={`Farbe ${name}`}
                                placeholder="Themefarbe oder #rrggbb"
                                value={b.color}
                                maxLength={7}
                                onChange={(e) =>
                                    patch(b.id, { color: e.target.value })
                                }
                            />
                        </label>
                        {b.iconKind === "Emoji" && (
                            <label>
                                Emoji
                                <Input
                                    aria-label={`Emoji ${name}`}
                                    value={b.iconValue}
                                    onChange={(e) =>
                                        patch(b.id, {
                                            iconValue: e.target.value,
                                        })
                                    }
                                />
                            </label>
                        )}
                        {b.iconKind === "Glyph" && (
                            <label>
                                Symbol{" "}
                                <select
                                    className={selectClass}
                                    aria-label={`Symbol ${name}`}
                                    value={b.iconValue.toLowerCase()}
                                    onChange={(e) =>
                                        patch(b.id, {
                                            iconValue: e.target.value,
                                        })
                                    }
                                >
                                    {!SCENE_GLYPHS[
                                        b.iconValue.toLowerCase()
                                    ] && (
                                        <option
                                            value={b.iconValue.toLowerCase()}
                                        >
                                            Altes Symbol (Video-Fallback)
                                        </option>
                                    )}
                                    {Object.entries(SCENE_GLYPHS).map(
                                        ([value, g]) => (
                                            <option value={value} key={value}>
                                                {g.symbol} {g.label}
                                            </option>
                                        ),
                                    )}
                                </select>
                            </label>
                        )}
                        {b.iconKind === "Image" && (
                            <div className="space-y-2 sm:col-span-2">
                                <label>
                                    Bildpfad
                                    <Input
                                        aria-label={`Bildpfad ${name}`}
                                        value={b.iconValue}
                                        onChange={(e) =>
                                            patch(b.id, {
                                                iconValue: e.target.value,
                                            })
                                        }
                                    />
                                </label>
                                <Button
                                    variant="ghost"
                                    onClick={async () => {
                                        try {
                                            setError(undefined);
                                            const path = await open({
                                                multiple: false,
                                                filters: [
                                                    {
                                                        name: "Bilder",
                                                        extensions: [
                                                            "png",
                                                            "jpg",
                                                            "jpeg",
                                                            "gif",
                                                            "webp",
                                                            "bmp",
                                                        ],
                                                    },
                                                ],
                                            });
                                            if (typeof path === "string")
                                                patch(b.id, {
                                                    iconValue: path,
                                                });
                                        } catch (e) {
                                            setError(String(e));
                                        }
                                    }}
                                >
                                    Bilddatei wählen
                                </Button>
                                <label>
                                    {" "}
                                    Bibliothek{" "}
                                    <select
                                        className={selectClass}
                                        aria-label={`Bibliotheksbild ${name}`}
                                        value=""
                                        onChange={(e) => {
                                            const asset = assets.data?.find(
                                                (a) => a.id === e.target.value,
                                            );
                                            if (asset)
                                                patch(b.id, {
                                                    iconValue: asset.path,
                                                });
                                        }}
                                    >
                                        <option value="">Bild auswählen</option>
                                        {assets.data?.map((a) => (
                                            <option key={a.id} value={a.id}>
                                                {a.name}
                                            </option>
                                        ))}
                                    </select>
                                </label>
                                {assets.isError && (
                                    <p role="alert" className="text-danger">
                                        {String(assets.error)}
                                    </p>
                                )}
                            </div>
                        )}
                    </div>
                );
            })}
            <Button
                variant="ghost"
                onClick={() =>
                    onChange((rows) => [
                        ...rows,
                        {
                            id: crypto.randomUUID().replace(/-/g, ""),
                            title: choices[0] || "Neue Szene",
                            sceneName: choices[0] || "",
                            iconKind: "Emoji",
                            iconValue: "🎬",
                            color: "",
                        },
                    ])
                }
            >
                Szenenbutton hinzufügen
            </Button>
            {error && (
                <p role="alert" className="text-danger">
                    {error}
                </p>
            )}
        </section>
    );
}
