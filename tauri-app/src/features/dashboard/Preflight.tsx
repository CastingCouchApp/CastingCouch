import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import type { PreflightSnapshot } from "../../lib/command-contract";
import { tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";

export function Preflight() {
    const [last, setLast] = useState<PreflightSnapshot>();
    const run = useMutation({
        mutationFn: () => tauriInvoke<PreflightSnapshot>("dashboard_preflight"),
        onSuccess: setLast,
    });
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Vorprüfung</h2>
            <p className="text-sm text-text-secondary">
                Prüft Verbindungen und gespeicherte Konfiguration. Startet
                keinen Stream.
            </p>
            <Button disabled={run.isPending} onClick={() => run.mutate()}>
                {run.isPending ? "Prüfung läuft …" : "Vorprüfung ausführen"}
            </Button>
            {run.error && <p role="alert">{String(run.error)}</p>}
            {last && (
                <>
                    <p>
                        {run.isError || run.isPending
                            ? "Vorherige Prüfung · "
                            : "Geprüft · "}
                        {new Date(last.checkedAt).toLocaleString()}
                    </p>
                    <p aria-live="polite">
                        {last.warningCount === 0
                            ? "Vorprüfung erfolgreich: Stream ist bereit."
                            : `${last.warningCount} ${last.warningCount === 1 ? "Punkt benötigt" : "Punkte benötigen"} Aufmerksamkeit.`}
                    </p>
                    <ul className="space-y-2">
                        {last.checks.map((check) => (
                            <li key={check.key}>
                                <span>
                                    {check.ok ? "✓" : "⚠"} {check.label}
                                </span>
                                {check.detail && (
                                    <p className="text-sm text-text-secondary">
                                        {check.detail}
                                    </p>
                                )}
                            </li>
                        ))}
                    </ul>
                </>
            )}
        </Card>
    );
}
