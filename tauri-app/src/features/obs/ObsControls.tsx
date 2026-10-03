import type { ObsControl } from "../../lib/command-contract";
type OutputAction = Exclude<ObsControl["action"], `set_${string}`>;
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { useState } from "react";
import { StreamEndPanel } from "../dashboard/StreamEndPanel";
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
    const [ending, setEnding] = useState(false);
    const client = useQueryClient();
    const status = useQuery({
        queryKey: ["obs-outputs"],
        queryFn: () => tauriInvoke<Outputs>("obs_output_status"),
        enabled,
        refetchInterval: 3000,
    });
    const control = useMutation({
        mutationFn: (action: OutputAction) =>
            tauriInvoke("obs_control", { control: { action } }),
        onSuccess: () =>
            void client.invalidateQueries({ queryKey: ["obs-outputs"] }),
    });
    const available = (action: OutputAction) => {
        const key = action.includes("replay")
            ? "replay"
            : action.includes("virtual")
              ? "camera"
              : action.includes("record")
                ? "record"
                : "stream";
        return (
            typeof status.data?.[key]?.outputActive === "boolean" &&
            !status.isError
        );
    };
    const button = (action: OutputAction, label: string) => (
        <Button
            key={action}
            disabled={!enabled || control.isPending || !available(action)}
            onClick={() => {
                if (action === "stop_stream") {
                    setEnding(true);
                } else if (
                    action !== "start_stream" ||
                    window.confirm("OBS-Stream wirklich starten?")
                ) {
                    control.mutate(action);
                }
            }}
        >
            {label}
        </Button>
    );
    return (
        <>
            <Card className="space-y-3">
                <h2 className="text-lg font-semibold">OBS-Ausgänge</h2>
                <p>
                    {!enabled
                        ? "OBS nicht verbunden"
                        : status.isError ||
                            typeof status.data?.stream?.outputActive !==
                                "boolean"
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
                {Object.entries(status.data?.errors ?? {}).map(
                    ([key, error]) => (
                        <p key={key} role="status">
                            {key}: {error}
                        </p>
                    ),
                )}
                {(status.error || control.error) && (
                    <p role="alert">{String(control.error ?? status.error)}</p>
                )}
            </Card>
            {ending && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
                    <div
                        role="dialog"
                        aria-modal="true"
                        aria-label="Streamende und Raid"
                        className="max-h-[90vh] w-full max-w-2xl overflow-auto"
                    >
                        <StreamEndPanel
                            enabled={enabled}
                            live={
                                !status.isError
                                    ? status.data?.stream?.outputActive
                                    : undefined
                            }
                            defaultExpanded
                            onClose={() => setEnding(false)}
                        />
                    </div>
                </div>
            )}
        </>
    );
}
