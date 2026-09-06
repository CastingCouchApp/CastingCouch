import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
export function StartupGate({ children }: { children: ReactNode }) {
    const startup = useQuery({
        queryKey: ["startup-error"],
        queryFn: () => tauriInvoke<string | null>("startup_error"),
        retry: false,
        staleTime: Infinity,
    });
    const overlay = useQuery({
        queryKey: ["overlay-runtime-status"],
        queryFn: () =>
            tauriInvoke<{ running: boolean; error: string | null }>(
                "overlay_runtime_status",
            ),
        enabled: startup.isSuccess && !startup.data,
        refetchInterval: 5000,
        retry: false,
    });
    if (startup.isPending) return <p>App wird gestartet…</p>;
    if (startup.error || startup.data)
        return (
            <main className="p-8">
                <h1>App konnte nicht gestartet werden</h1>
                <p role="alert">{String(startup.data ?? startup.error)}</p>
                <p>
                    Bitte den angegebenen Fehler beheben und die App erneut
                    starten. Details stehen im Logs-Ordner der App-Daten.
                </p>
            </main>
        );
    return (
        <>
            {overlay.data && (!overlay.data.running || overlay.data.error) && (
                <div role="alert" className="bg-red-950 p-3 text-white">
                    Overlay-Problem:{" "}
                    {overlay.data.error ?? "Verbindung wird geprüft"}. Port und
                    Datenpfad unter Einstellungen prüfen. Die App versucht den
                    Start erneut.
                </div>
            )}
            {children}
        </>
    );
}
