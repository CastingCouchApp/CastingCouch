import { useEffect, useState } from "react";
import { listen } from "@tauri-apps/api/event";
export function AppCloseNotice() {
    const [error, setError] = useState<string>();
    useEffect(() => {
        if (!("__TAURI_INTERNALS__" in window)) return;
        let disposed = false;
        let stop: (() => void) | undefined;
        void listen<string>("app-close-error", (event) =>
            setError(event.payload),
        )
            .then((unlisten) => {
                if (disposed) unlisten();
                else stop = unlisten;
            })
            .catch(() => {});
        return () => {
            disposed = true;
            stop?.();
        };
    }, []);
    return error ? (
        <div role="alert" className="mb-4 rounded border border-amber-500 p-3">
            <p>App konnte noch nicht beendet werden: {error}</p>
            <button
                className="mt-2 underline"
                onClick={() => setError(undefined)}
            >
                Meldung schließen
            </button>
        </div>
    ) : null;
}
