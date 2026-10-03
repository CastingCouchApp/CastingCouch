import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys, tauriInvoke } from "../../lib/api";
import type { SceneButtonDraft } from "../../lib/command-contract";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { useEffect, useState } from "react";

// The Windows-only Segoe MDL2 font is replaced visually; persisted codes stay intact.
export const SCENE_GLYPHS: Record<string, { label: string; symbol: string }> = {
    "\ue768": { label: "Play", symbol: "▶" },
    "\ue769": { label: "Pause", symbol: "⏸" },
    "\ue71a": { label: "Stop", symbol: "⏹" },
    "\ue80f": { label: "Home", symbol: "⌂" },
    "\ue722": { label: "Kamera", symbol: "📷" },
    "\ue714": { label: "Video", symbol: "🎬" },
    "\ue7fc": { label: "Spiel", symbol: "🎮" },
    "\ue734": { label: "Stern", symbol: "★" },
    "\ue8d6": { label: "Musik", symbol: "♫" },
    "\ue720": { label: "Mikrofon", symbol: "🎤" },
    "\ue93e": { label: "Live", symbol: "🔴" },
    "\ue7c1": { label: "Flagge", symbol: "⚑" },
};
export function SceneIcon({ button }: { button: SceneButtonDraft }) {
    const [failed, setFailed] = useState(false);
    const image = useQuery({
        queryKey: ["dashboard-image", button.iconValue],
        queryFn: () =>
            tauriInvoke<string>("dashboard_image_preview", {
                path: button.iconValue,
            }),
        enabled: button.iconKind === "Image" && Boolean(button.iconValue),
        staleTime: 15000,
        retry: false,
    });
    useEffect(() => setFailed(false), [button.iconValue, image.data]);
    if (button.iconKind === "Image") {
        const problem = failed
            ? "Bild konnte nicht angezeigt werden"
            : image.isError
              ? String(image.error)
              : !button.iconValue
                ? "Kein Bild ausgewählt"
                : "Bild wird geladen";
        return image.data && !failed ? (
            <img
                alt=""
                src={image.data}
                className="h-5 w-5 object-contain"
                onError={() => setFailed(true)}
            />
        ) : (
            <span role="img" aria-label={problem} title={problem}>
                🖼
            </span>
        );
    }
    return (
        <span aria-hidden="true">
            {button.iconKind === "Glyph"
                ? (SCENE_GLYPHS[button.iconValue.toLowerCase()]?.symbol ?? "🎬")
                : button.iconKind === "Emoji"
                  ? button.iconValue || "🎬"
                  : "🎬"}
        </span>
    );
}
export function DashboardSceneButtons({
    buttons,
    enabled,
    currentScene,
}: {
    buttons: SceneButtonDraft[];
    enabled: boolean;
    currentScene?: string | null;
}) {
    const client = useQueryClient();
    const change = useMutation({
        mutationFn: (scene: string) => tauriInvoke("obs_set_scene", { scene }),
        onSuccess: () =>
            void client.invalidateQueries({
                queryKey: queryKeys.obsCurrentScene,
            }),
    });
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Szenen</h2>
            {!enabled && <p className="text-muted">OBS nicht verbunden</p>}
            {!buttons.length && (
                <p className="text-muted">
                    Noch keine Szenenbuttons. Im Dashboard-Editor hinzufügen.
                </p>
            )}
            <div className="flex flex-wrap gap-2">
                {buttons.map((b) => (
                    <Button
                        key={b.id}
                        variant="ghost"
                        className="gap-2 border-2"
                        style={{
                            borderColor:
                                enabled &&
                                currentScene?.toLowerCase() ===
                                    b.sceneName.toLowerCase()
                                    ? b.color || "var(--color-brand)"
                                    : "transparent",
                            color: b.color || undefined,
                        }}
                        aria-pressed={
                            enabled &&
                            currentScene?.toLowerCase() ===
                                b.sceneName.toLowerCase()
                        }
                        disabled={!enabled || !b.sceneName || change.isPending}
                        onClick={() => change.mutate(b.sceneName)}
                    >
                        <SceneIcon button={b} />
                        {b.title || b.sceneName}
                    </Button>
                ))}
            </div>
            {change.isError && (
                <p role="alert" className="text-danger">
                    {String(
                        change.error instanceof Error
                            ? change.error.message
                            : change.error,
                    )}
                </p>
            )}
        </Card>
    );
}
