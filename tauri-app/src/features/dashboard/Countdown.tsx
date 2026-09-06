import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Card } from "../../components/ui/card";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
export function Countdown() {
    const client = useQueryClient();
    const [seconds, setSeconds] = useState(300);
    const [label, setLabel] = useState("Gleich geht es los");
    const status = useQuery({
        queryKey: ["countdown"],
        queryFn: () =>
            tauriInvoke<{
                data: { remainingSeconds: string; isRunning: string };
            }>("countdown_status"),
        refetchInterval: 1000,
    });
    const change = useMutation({
        mutationFn: (value: number) =>
            tauriInvoke("set_countdown", { seconds: value, label }),
        onSuccess: () =>
            void client.invalidateQueries({ queryKey: ["countdown"] }),
    });
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">Overlay-Countdown</h2>
            <p className="text-3xl tabular-nums">
                {status.data?.data?.remainingSeconds ?? "0"} s
            </p>
            <label className="block">
                Dauer in Sekunden
                <Input
                    type="number"
                    min="1"
                    max="86400"
                    value={seconds}
                    onChange={(e) => setSeconds(Number(e.target.value))}
                />
            </label>
            <label className="block">
                Beschriftung
                <Input
                    value={label}
                    onChange={(e) => setLabel(e.target.value)}
                />
            </label>
            <div className="flex gap-2">
                <Button
                    disabled={
                        change.isPending || seconds < 1 || seconds > 86400
                    }
                    onClick={() => change.mutate(seconds)}
                >
                    Countdown starten
                </Button>
                <Button
                    disabled={change.isPending}
                    onClick={() => change.mutate(0)}
                >
                    Countdown stoppen
                </Button>
            </div>
            {change.error && <p role="alert">{String(change.error)}</p>}
        </Card>
    );
}
