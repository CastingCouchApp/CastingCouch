import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { tauriInvoke, type CanvasDto } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
export function ObsSourceSetup({ canvases }: { canvases: CanvasDto[] }) {
    const [canvas, setCanvas] = useState("");
    const [scene, setScene] = useState("");
    const [name, setName] = useState("CCS Canvas");
    const selected = canvases.find((c) => c.id === canvas) ?? canvases[0];
    const scenes = useQuery({
        queryKey: ["obs-scenes"],
        queryFn: () =>
            tauriInvoke<{ name: string; index: number }[]>("obs_scenes"),
        retry: false,
    });
    const setup = useMutation({
        mutationFn: () =>
            tauriInvoke<{ created: boolean }>("setup_overlay_source", {
                canvasId: selected?.id,
                sceneName: scene,
                inputName: name.trim(),
            }),
    });
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Overlay in OBS einrichten</h2>
            <p className="text-sm text-text-secondary">
                Wähle Canvas, Zielszene und Quellenname. Bei einer vorhandenen
                Browserquelle werden URL und Größe aktualisiert; ihre Position
                und Sichtbarkeit bleiben erhalten.
            </p>
            <form
                className="space-y-3"
                onSubmit={(e) => {
                    e.preventDefault();
                    setup.mutate();
                }}
            >
                <label className="block">
                    Overlay-Canvas{" "}
                    <select
                        aria-label="Overlay-Canvas"
                        className="rounded border border-border bg-panel p-2"
                        value={selected?.id ?? ""}
                        onChange={(e) => {
                            setCanvas(e.target.value);
                            setup.reset();
                        }}
                    >
                        {canvases.map((c) => (
                            <option key={c.id} value={c.id}>
                                {c.name}
                            </option>
                        ))}
                    </select>
                </label>
                <label className="block">
                    OBS-Zielszene{" "}
                    <select
                        aria-label="OBS-Zielszene"
                        className="rounded border border-border bg-panel p-2"
                        value={scene}
                        onChange={(e) => {
                            setScene(e.target.value);
                            setup.reset();
                        }}
                    >
                        <option value="">Szene auswählen</option>
                        {scenes.data?.map((s) => (
                            <option key={s.name} value={s.name}>
                                {s.name}
                            </option>
                        ))}
                    </select>
                </label>
                <label className="block">
                    OBS-Quellenname{" "}
                    <Input
                        value={name}
                        onChange={(e) => {
                            setName(e.target.value);
                            setup.reset();
                        }}
                    />
                </label>
                {selected && (
                    <p className="break-all text-sm">{selected.view_url}</p>
                )}
                <div className="flex gap-2">
                    <Button
                        type="submit"
                        disabled={
                            !selected ||
                            !name.trim() ||
                            !scene ||
                            setup.isPending ||
                            scenes.isError
                        }
                    >
                        Browserquelle anlegen / aktualisieren
                    </Button>
                    <Button
                        type="button"
                        variant="ghost"
                        onClick={() => void scenes.refetch()}
                    >
                        Szenen neu laden
                    </Button>
                </div>
            </form>
            {setup.data && (
                <p role="status">
                    {setup.data.created
                        ? "Browserquelle angelegt"
                        : "Browserquelle aktualisiert"}
                </p>
            )}
            {(setup.error || scenes.error) && (
                <p role="alert">{String(setup.error ?? scenes.error)}</p>
            )}
        </Card>
    );
}
