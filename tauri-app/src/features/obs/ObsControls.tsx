import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
type Output = {
    outputActive?: boolean;
    outputPaused?: boolean;
    outputDuration?: number;
};
type Outputs = {
    stream: Output;
    record: Output | null;
    replay: Output | null;
    camera: Output | null;
    stats: { activeFps?: number; cpuUsage?: number } | null;
    errors?: Record<string, string>;
};
export function ObsControls({ enabled }: { enabled: boolean }) {
    const client = useQueryClient();
    const status = useQuery({
        queryKey: ["obs-outputs"],
        queryFn: () => tauriInvoke<Outputs>("obs_output_status"),
        enabled,
        refetchInterval: 3000,
    });
    const control = useMutation({
        mutationFn: (action: string) =>
            tauriInvoke("obs_control", { control: { action } }),
        onSuccess: () =>
            void client.invalidateQueries({ queryKey: ["obs-outputs"] }),
    });
    const available = (action: string) => {
        const key = action.includes("replay")
            ? "replay"
            : action.includes("virtual")
              ? "camera"
              : action.includes("record")
                ? "record"
                : "stream";
        return Boolean(status.data?.[key]) && !status.isError;
    };
    const button = (action: string, label: string) => (
        <Button
            key={action}
            disabled={!enabled || control.isPending || !available(action)}
            onClick={() => control.mutate(action)}
        >
            {label}
        </Button>
    );
    return (
        <Card className="space-y-3">
            <h2 className="text-lg font-semibold">OBS-Ausgänge</h2>
            <p>
                {!enabled
                    ? "OBS nicht verbunden"
                    : status.isError || !status.data?.stream
                      ? "Streamstatus unbekannt"
                      : status.data.stream.outputActive
                        ? "Stream läuft"
                        : "Stream gestoppt"}
            </p>
            <div className="flex flex-wrap gap-2">
                {button(
                    status.data?.stream?.outputActive
                        ? "stop_stream"
                        : "start_stream",
                    status.data?.stream?.outputActive
                        ? "Stream stoppen"
                        : "Stream starten",
                )}
                {button(
                    status.data?.record?.outputActive
                        ? "stop_record"
                        : "start_record",
                    status.data?.record?.outputActive
                        ? "Aufnahme stoppen"
                        : "Aufnahme starten",
                )}
                {status.data?.record?.outputActive &&
                    button(
                        status.data.record.outputPaused
                            ? "resume_record"
                            : "pause_record",
                        status.data.record.outputPaused
                            ? "Aufnahme fortsetzen"
                            : "Aufnahme pausieren",
                    )}
            </div>
            <div className="flex flex-wrap gap-2">
                {button(
                    status.data?.replay?.outputActive
                        ? "stop_replay_buffer"
                        : "start_replay_buffer",
                    status.data?.replay?.outputActive
                        ? "Replay Buffer stoppen"
                        : "Replay Buffer starten",
                )}
                {status.data?.replay?.outputActive &&
                    button("save_replay_buffer", "Replay speichern")}
                {button(
                    status.data?.camera?.outputActive
                        ? "stop_virtual_cam"
                        : "start_virtual_cam",
                    status.data?.camera?.outputActive
                        ? "Virtuelle Kamera stoppen"
                        : "Virtuelle Kamera starten",
                )}
            </div>
            {status.data?.stats && (
                <p className="text-sm text-text-secondary">
                    FPS {status.data.stats.activeFps?.toFixed(1)} · CPU{" "}
                    {status.data.stats.cpuUsage?.toFixed(1)} %
                </p>
            )}
            {Object.entries(status.data?.errors ?? {}).map(([key, error]) => (
                <p key={key} role="status">
                    {key}: {error}
                </p>
            ))}
            {(status.error || control.error) && (
                <p role="alert">{String(control.error ?? status.error)}</p>
            )}
        </Card>
    );
}
