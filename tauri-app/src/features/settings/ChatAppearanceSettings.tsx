import { useEffect, useRef, useState } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import type { OverlayChatSettings } from "../../lib/app-settings";

const numbers = [
    ["BackgroundOpacity", "Chat-Hintergrunddeckkraft (%)", 0.55, 0, 100, 100],
    ["FontSizePx", "Chat-Schriftgröße (px)", 18, 8, 72, 1],
    ["PaddingPx", "Chat-Innenabstand (px)", 12, 0, 120, 1],
    ["BorderRadiusPx", "Chat-Eckenradius (px)", 12, 0, 64, 1],
    ["GapPx", "Chat-Zeilenabstand (px)", 6, 0, 48, 1],
] as const;

function NumericField({
    label,
    value,
    min,
    max,
    onChange,
}: {
    label: string;
    value: number;
    min: number;
    max: number;
    onChange: (value: number) => void;
}) {
    const bounded = (n: number) => Math.min(max, Math.max(min, n));
    const [draft, setDraft] = useState(String(bounded(value)));
    const emitted = useRef<number | null>(null);
    useEffect(() => {
        if (emitted.current !== value)
            setDraft(String(Math.min(max, Math.max(min, value))));
        emitted.current = null;
    }, [value, min, max]);
    return (
        <label className="block text-sm">
            {label}
            <Input
                type="number"
                value={draft}
                min={min}
                max={max}
                step={1}
                onChange={(e) => {
                    setDraft(e.target.value);
                    if (
                        e.target.value !== "" &&
                        Number.isFinite(e.target.valueAsNumber)
                    ) {
                        emitted.current = e.target.valueAsNumber;
                        onChange(e.target.valueAsNumber);
                    }
                }}
                onBlur={() => {
                    const n = draft.trim() ? Number(draft) : bounded(value);
                    if (Number.isFinite(n)) {
                        setDraft(String(bounded(n)));
                        if (n !== bounded(n)) onChange(bounded(n));
                    }
                }}
            />
        </label>
    );
}

export function ChatAppearanceSettings({
    value,
    onChange,
}: {
    value: OverlayChatSettings;
    onChange: (value: OverlayChatSettings) => void;
}) {
    const [error, setError] = useState("");
    const [browsing, setBrowsing] = useState(false);
    const latest = useRef(value);
    latest.current = value;
    const patch = (changes: Partial<OverlayChatSettings>) =>
        onChange({ ...latest.current, ...changes });
    const backgroundType = value.BackgroundType?.trim().toLowerCase();
    const type =
        backgroundType === "color"
            ? "Color"
            : backgroundType === "image"
              ? "Image"
              : "None";
    async function chooseImage() {
        setError("");
        setBrowsing(true);
        try {
            const path = await open({
                multiple: false,
                directory: false,
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
                            "svg",
                        ],
                    },
                ],
            });
            if (typeof path === "string")
                patch({ BackgroundImagePath: path, BackgroundType: "Image" });
        } catch (e) {
            setError(e instanceof Error ? e.message : String(e));
        } finally {
            setBrowsing(false);
        }
    }
    return (
        <fieldset className="space-y-3 border-t border-border pt-3">
            <legend className="font-medium">Chat-Erscheinungsbild</legend>
            <label className="block text-sm">
                Chat-Hintergrund
                <select
                    className="mt-1 w-full rounded-md border border-border bg-card px-3 py-1.5 text-sm"
                    value={type}
                    onChange={(e) => patch({ BackgroundType: e.target.value })}
                >
                    <option value="None">Transparent</option>
                    <option value="Color">Farbe</option>
                    <option value="Image">Bild</option>
                </select>
            </label>
            <label className="block text-sm">
                Chat-Hintergrundfarbe
                <Input
                    value={value.BackgroundColor ?? "#000000"}
                    onChange={(e) => patch({ BackgroundColor: e.target.value })}
                />
            </label>
            <label className="block text-sm">
                Chat-Hintergrundbild
                <Input
                    value={value.BackgroundImagePath ?? ""}
                    onChange={(e) =>
                        patch({ BackgroundImagePath: e.target.value })
                    }
                />
            </label>
            <Button
                type="button"
                disabled={browsing}
                onClick={() => void chooseImage()}
            >
                Chat-Hintergrundbild auswählen
            </Button>
            {error && (
                <p role="alert" className="text-sm text-red-400">
                    {error}
                </p>
            )}
            <label className="block text-sm">
                Chat-Schrift
                <Input
                    list="chat-fonts"
                    value={
                        value.FontFamily ?? "Segoe UI, system-ui, sans-serif"
                    }
                    onChange={(e) => patch({ FontFamily: e.target.value })}
                />
            </label>
            <datalist id="chat-fonts">
                {[
                    "Segoe UI, system-ui, sans-serif",
                    "Arial",
                    "Verdana",
                    "Tahoma",
                    "Comic Sans MS",
                    "monospace",
                ].map((font) => (
                    <option key={font} value={font} />
                ))}
            </datalist>
            <div className="grid gap-3 sm:grid-cols-2">
                {numbers.map(([key, label, fallback, min, max, scale]) => (
                    <NumericField
                        key={key}
                        label={label}
                        value={Math.round((value[key] ?? fallback) * scale)}
                        min={min}
                        max={max}
                        onChange={(n) => patch({ [key]: n / scale })}
                    />
                ))}
            </div>
            <p className="text-xs text-text-secondary">
                Die Deckkraft betrifft nur den Hintergrund. Canvas- und
                Solo-Widgets können eigene Schrift- und Hintergrundwerte
                verwenden. Fehlende Bilddateien ergeben einen transparenten
                Hintergrund.
            </p>
        </fieldset>
    );
}
