import { useState } from "react";
import { tauriInvoke, type AlertDefinition } from "../../lib/api";
import { useMutation } from "@tanstack/react-query";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Button } from "../../components/ui/button";
export function AlertEditor({
    initial,
    onSave,
    onClose,
    pending,
}: {
    initial: AlertDefinition;
    onSave: (value: AlertDefinition) => void;
    onClose: () => void;
    pending: boolean;
}) {
    const [draft, setDraft] = useState(() => structuredClone(initial));
    const preview = useMutation({
        mutationFn: (alert: AlertDefinition) =>
            tauriInvoke<{
                text: string;
                media: { url: string; mime: string } | null;
                sound: { url: string; mime: string } | null;
            }>("alert_preview", { alert, user: "Testnutzer" }),
    });
    const currentPreview =
        JSON.stringify(preview.variables) === JSON.stringify(draft)
            ? preview.data
            : undefined;
    const [mediaError, setMediaError] = useState("");
    const textFields = [
        ["type", "Name / Typ"],
        ["text_template", "Textvorlage"],
        ["media_path", "Medienpfad (auf dem OBS-Rechner)"],
        ["sound_path", "Soundpfad (lokale Datei)"],
        ["font_face", "Schriftart"],
    ] as const;
    const numberFields = [
        ["duration_seconds", "Dauer (Sekunden)", 0, 3600],
        ["priority", "Priorität", 0, 1000],
        ["font_size", "Schriftgröße", 8, 300],
        ["x", "Position X", -7680, 7680],
        ["y", "Position Y", -4320, 4320],
        ["width", "Breite", 1, 7680],
        ["height", "Höhe", 1, 4320],
        ["volume_percent", "Medienlautstärke", 0, 100],
        ["sound_start_seconds", "Sound-Start (Sekunden)", 0, 86400],
        ["sound_end_seconds", "Sound-Ende (Sekunden, 0 = Dateiende)", 0, 86400],
    ] as const;
    return (
        <Card className="space-y-4">
            <div className="flex justify-between">
                <h2 className="text-lg font-semibold">
                    Alert bearbeiten: {draft.type}
                </h2>
                <Button onClick={onClose}>Schließen</Button>
            </div>
            <form
                className="grid gap-3 md:grid-cols-2"
                onSubmit={(e) => {
                    e.preventDefault();
                    onSave(draft);
                }}
            >
                {textFields.map(([key, label]) => (
                    <label key={key} className="block">
                        {label}
                        <Input
                            required={key === "type"}
                            value={draft[key]}
                            onChange={(e) =>
                                setDraft({ ...draft, [key]: e.target.value })
                            }
                        />
                    </label>
                ))}
                {numberFields.map(([key, label, min, max]) => (
                    <label key={key} className="block">
                        {label}
                        <Input
                            type="number"
                            step={key.startsWith("sound_") ? 0.01 : 1}
                            min={min}
                            max={max}
                            value={draft[key]}
                            onChange={(e) =>
                                setDraft({
                                    ...draft,
                                    [key]: Number(e.target.value),
                                })
                            }
                        />
                    </label>
                ))}
                <label className="block">
                    Animation
                    <select
                        className="block w-full rounded border border-border bg-input p-2"
                        value={draft.animation}
                        onChange={(e) =>
                            setDraft({ ...draft, animation: e.target.value })
                        }
                    >
                        {["Fade", "Slide", "Zoom", "Bounce"].map(
                            (animation) => (
                                <option key={animation}>{animation}</option>
                            ),
                        )}
                    </select>
                </label>
                <label className="flex items-center gap-2">
                    <input
                        type="checkbox"
                        checked={draft.enabled}
                        onChange={(e) =>
                            setDraft({ ...draft, enabled: e.target.checked })
                        }
                    />
                    Alert aktiv
                </label>
                <label className="block">
                    Schriftfarbe
                    <input
                        className="block h-10 w-full"
                        type="color"
                        value={draft.font_color}
                        onChange={(e) =>
                            setDraft({ ...draft, font_color: e.target.value })
                        }
                    />
                </label>
                <div
                    className="md:col-span-2 rounded border border-border bg-black p-4"
                    style={{
                        color: draft.font_color,
                        fontFamily: draft.font_face,
                        fontSize: Math.min(72, draft.font_size),
                    }}
                >
                    {currentPreview?.text ??
                        draft.text_template
                            .split("{user}")
                            .join("Testnutzer")
                            .split("{bits}")
                            .join("100")}
                </div>
                <div className="md:col-span-2 space-y-2">
                    <Button
                        type="button"
                        disabled={preview.isPending}
                        onClick={() => {
                            setMediaError("");
                            preview.mutate(draft);
                        }}
                    >
                        Vorschau laden
                    </Button>
                    <p className="text-sm text-muted-foreground">
                        Lokale Medienvorschau mit Sound-Ausschnitt. Bei
                        Änderungen die Vorschau erneut laden.
                    </p>
                    {currentPreview?.media &&
                        (currentPreview.media.mime.startsWith("image/") ? (
                            <img
                                className="max-h-64 max-w-full object-contain"
                                src={currentPreview.media.url}
                                alt="Alert-Medienvorschau"
                                onError={() =>
                                    setMediaError(
                                        "Das Bild konnte nicht dargestellt werden.",
                                    )
                                }
                            />
                        ) : (
                            <video
                                className="max-h-64 max-w-full"
                                src={currentPreview.media.url}
                                controls
                                onError={() =>
                                    setMediaError(
                                        "Dieses Medienformat kann die lokale Vorschau nicht abspielen.",
                                    )
                                }
                                onLoadedMetadata={(e) => {
                                    e.currentTarget.volume =
                                        draft.volume_percent / 100;
                                }}
                            />
                        ))}
                    {currentPreview?.sound && (
                        <audio
                            aria-label="Alert-Soundvorschau"
                            src={currentPreview.sound.url}
                            controls
                            onError={() =>
                                setMediaError(
                                    "Dieses Audioformat kann die lokale Vorschau nicht abspielen.",
                                )
                            }
                            onLoadedMetadata={(e) => {
                                e.currentTarget.currentTime =
                                    draft.sound_start_seconds;
                                e.currentTarget.volume =
                                    draft.volume_percent / 100;
                            }}
                            onPlay={(e) => {
                                if (
                                    e.currentTarget.currentTime <
                                        draft.sound_start_seconds ||
                                    (draft.sound_end_seconds > 0 &&
                                        e.currentTarget.currentTime >=
                                            draft.sound_end_seconds)
                                )
                                    e.currentTarget.currentTime =
                                        draft.sound_start_seconds;
                            }}
                            onTimeUpdate={(e) => {
                                if (
                                    draft.sound_end_seconds > 0 &&
                                    e.currentTarget.currentTime >=
                                        draft.sound_end_seconds
                                )
                                    e.currentTarget.pause();
                            }}
                        />
                    )}
                    {preview.error && (
                        <p role="alert">{String(preview.error)}</p>
                    )}
                    {mediaError && <p role="alert">{mediaError}</p>}
                </div>
                <Button type="submit" disabled={pending}>
                    Alert speichern
                </Button>
            </form>
        </Card>
    );
}
