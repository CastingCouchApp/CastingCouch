import { useState } from "react";
import { Button } from "../../components/ui/button";
import type { Apply, Control } from "./management-api";

export function SourceMediaControls({
    input,
    kind,
    apply,
}: {
    input: string;
    kind?: string;
    apply: Apply;
}) {
    const [pending, setPending] = useState(false);
    const [error, setError] = useState("");
    const [message, setMessage] = useState("");
    const normalized = kind?.toLowerCase() ?? "";
    const media = ["ffmpeg", "vlc", "media"].some((part) =>
        normalized.includes(part),
    );
    const browser = normalized.includes("browser");
    async function run(control: Control, success: string) {
        setPending(true);
        setError("");
        setMessage("");
        try {
            await apply(control);
            setMessage(success);
        } catch (error) {
            setError(error instanceof Error ? error.message : String(error));
        } finally {
            setPending(false);
        }
    }
    if (!kind)
        return (
            <p className="text-text-secondary">
                Quellenart unbekannt; zuerst OBS-Verwaltung aktualisieren.
            </p>
        );
    if (!media && !browser) return null;
    return (
        <div className="space-y-2">
            <h4 className="font-medium">
                {browser ? "Browserquelle" : "Medienquelle"}
            </h4>
            <div className="flex flex-wrap gap-2">
                {media && (
                    <>
                        <Button
                            disabled={pending}
                            onClick={() =>
                                void run(
                                    {
                                        action: "restart_media",
                                        inputName: input,
                                    },
                                    "Medienneustart angefordert.",
                                )
                            }
                        >
                            Medien neu starten
                        </Button>
                        <Button
                            disabled={pending}
                            onClick={() =>
                                void run(
                                    { action: "stop_media", inputName: input },
                                    "Medienstopp angefordert.",
                                )
                            }
                        >
                            Medien stoppen
                        </Button>
                    </>
                )}
                {browser && (
                    <Button
                        disabled={pending}
                        onClick={() =>
                            void run(
                                { action: "refresh_browser", inputName: input },
                                "Browser-Neuladen ohne Cache angefordert.",
                            )
                        }
                    >
                        Browser ohne Cache neu laden
                    </Button>
                )}
            </div>
            {error && <p role="alert">{error}</p>}
            {message && <p role="status">{message}</p>}
        </div>
    );
}
