import { useState } from "react";
import type { AlertDefinition } from "../../lib/api";
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
    const textFields = [
        ["text_template", "Textvorlage"],
        ["media_path", "Medienpfad (auf dem OBS-Rechner)"],
        ["font_face", "Schriftart"],
    ] as const;
    const numberFields = [
        ["duration_seconds", "Dauer (Sekunden)", 0, 3600],
        ["font_size", "Schriftgröße", 8, 300],
        ["x", "Position X", -7680, 7680],
        ["y", "Position Y", -4320, 4320],
        ["width", "Breite", 1, 7680],
        ["height", "Höhe", 1, 4320],
        ["volume_percent", "Medienlautstärke", 0, 100],
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
                    {draft.text_template
                        .split("{user}")
                        .join("Testnutzer")
                        .split("{bits}")
                        .join("100")}
                </div>
                <Button type="submit" disabled={pending}>
                    Alert speichern
                </Button>
            </form>
        </Card>
    );
}
