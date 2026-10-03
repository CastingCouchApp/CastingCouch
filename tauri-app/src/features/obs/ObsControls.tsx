import type { ObsControl } from "../../lib/command-contract";
type OutputAction = Extract<
    ObsControl["action"],
    | "start_stream"
    | "stop_stream"
    | "start_record"
    | "stop_record"
    | "pause_record"
    | "resume_record"
    | "start_replay_buffer"
    | "stop_replay_buffer"
    | "save_replay_buffer"
    | "start_virtual_cam"
    | "stop_virtual_cam"
>;
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { useState } from "react";
import { StreamEndPanel } from "../dashboard/StreamEndPanel";
import { ObsMonitoring, type ObsOutputs } from "./ObsMonitoring";
export function ObsControls({ enabled }: { enabled: boolean }) {
    const [ending, setEnding] = useState(false);
    const client = useQueryClient();
    const status = useQuery({
        queryKey: ["obs-outputs"],
        queryFn: () => tauriInvoke<ObsOutputs>("obs_output_status"),
        enabled,
        refetchInterval: 3000,
    });
    const control = useMutation({
        mutationFn: (action: OutputAction) =>
            tauriInvoke("obs_control", { control: { action } }),
        onSuccess: () =>
            void client.invalidateQueries({ queryKey: ["obs-outputs"] }),
    });
    const data = enabled && !status.isError ? status.data : undefined;
    const available = (action: OutputAction) => {
        const key = action.includes("replay")
            ? "replay"
            : action.includes("virtual")
              ? "camera"
              : action.includes("record")
                ? "record"
                : "stream";
        return (
            typeof data?.[key]?.outputActive === "boolean" &&
            (!(action === "pause_record" || action === "resume_record") ||
                typeof data?.record?.outputPaused === "boolean")
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
                            typeof data?.stream?.outputActive !== "boolean"
                          ? "Streamstatus unbekannt"
                          : data.stream.outputActive
                            ? "Stream läuft"
                            : "Stream gestoppt"}
                </p>
                <div className="flex flex-wrap gap-2">
                    {button(
                        data?.stream?.outputActive
                            ? "stop_stream"
                            : "start_stream",
                        data?.stream?.outputActive
                            ? "Stream stoppen"
                            : "Stream starten",
                    )}
                    {button(
                        data?.record?.outputActive
                            ? "stop_record"
                            : "start_record",
                        data?.record?.outputActive
                            ? "Aufnahme stoppen"
                            : "Aufnahme starten",
                    )}
                    {data?.record?.outputActive &&
                        button(
                            data.record.outputPaused
                                ? "resume_record"
                                : "pause_record",
                            data.record.outputPaused
                                ? "Aufnahme fortsetzen"
                                : "Aufnahme pausieren",
                        )}
                </div>
                <div className="flex flex-wrap gap-2">
                    {button(
                        data?.replay?.outputActive
                            ? "stop_replay_buffer"
                            : "start_replay_buffer",
                        data?.replay?.outputActive
                            ? "Replay Buffer stoppen"
                            : "Replay Buffer starten",
                    )}
                    {data?.replay?.outputActive &&
                        button("save_replay_buffer", "Replay speichern")}
                    {button(
                        data?.camera?.outputActive
                            ? "stop_virtual_cam"
                            : "start_virtual_cam",
                        data?.camera?.outputActive
                            ? "Virtuelle Kamera stoppen"
                            : "Virtuelle Kamera starten",
                    )}
                </div>
                <ObsMonitoring outputs={data} />
                <Button
                    variant="ghost"
                    disabled={
                        !enabled || status.isFetching || control.isPending
                    }
                    onClick={() => void status.refetch()}
                >
                    OBS-Status aktualisieren
                </Button>
                {Object.entries(data?.errors ?? {}).map(([key, error]) => (
                    <p key={key} role="status">
                        {key}: {error}
                    </p>
                ))}
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
                                    ? data?.stream?.outputActive
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
