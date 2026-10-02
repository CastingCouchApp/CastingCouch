import { useEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { listenTwitchEvents } from "../../lib/api";

// Coalesce progress bursts; periodic queries also recover missed events after reconnect.
export function useTwitchRefresh(
    enabled: boolean,
    prefix: string,
    key: string,
) {
    const client = useQueryClient();
    const [error, setError] = useState<string>();
    useEffect(() => {
        if (!enabled) return;
        let disposed = false;
        let unlisten: (() => void) | undefined;
        let timer: ReturnType<typeof setTimeout> | undefined;
        setError(undefined);
        void listenTwitchEvents((event) => {
            if (
                disposed ||
                typeof event.type !== "string" ||
                !event.type.startsWith(prefix) ||
                timer
            )
                return;
            timer = setTimeout(() => {
                timer = undefined;
                void client.invalidateQueries({ queryKey: [key] });
            }, 150);
        })
            .then((fn) => {
                if (disposed) fn();
                else unlisten = fn;
            })
            .catch((e) => {
                if (!disposed) setError(String(e));
            });
        return () => {
            disposed = true;
            unlisten?.();
            if (timer) clearTimeout(timer);
        };
    }, [enabled, prefix, key, client]);
    return error;
}
