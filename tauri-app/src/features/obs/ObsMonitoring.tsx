export type ObsOutput = {
    outputActive?: boolean;
    outputPaused?: boolean;
    outputDuration?: number;
    outputTimecode?: string;
};
export type ObsOutputs = {
    stream: ObsOutput | null;
    record: ObsOutput | null;
    replay: ObsOutput | null;
    camera: ObsOutput | null;
    stats: {
        activeFps?: number;
        cpuUsage?: number;
        memoryUsage?: number;
        renderSkippedFrames?: number;
        renderTotalFrames?: number;
        outputSkippedFrames?: number;
        outputTotalFrames?: number;
    } | null;
    errors?: Record<string, string>;
};
function known(value: unknown): value is number {
    return typeof value === "number" && Number.isFinite(value) && value >= 0;
}
function clock(output: ObsOutput): string {
    if (
        typeof output.outputTimecode === "string" &&
        output.outputTimecode.trim()
    )
        return output.outputTimecode;
    if (!known(output.outputDuration)) return "Laufzeit unbekannt";
    const seconds = Math.floor(output.outputDuration / 1000);
    return [
        Math.floor(seconds / 3600),
        Math.floor(seconds / 60) % 60,
        seconds % 60,
    ]
        .map((part) => String(part).padStart(2, "0"))
        .join(":");
}
function outputState(output: ObsOutput | null | undefined): string {
    return typeof output?.outputActive !== "boolean"
        ? "Status unbekannt"
        : output.outputActive
          ? "Aktiv"
          : "Gestoppt";
}
function recordState(output: ObsOutput | null | undefined): string {
    if (typeof output?.outputActive !== "boolean") return "Status unbekannt";
    if (!output.outputActive) return "Gestoppt";
    if (typeof output.outputPaused !== "boolean") return "Status unbekannt";
    return `${output.outputPaused ? "Pausiert" : "Läuft"} · ${clock(output)}`;
}
function metric(value: unknown, decimals: number, suffix = ""): string {
    return known(value) ? `${value.toFixed(decimals)}${suffix}` : "Unbekannt";
}
function frames(skipped: unknown, total: unknown): string {
    return known(skipped) && known(total) ? `${skipped}/${total}` : "Unbekannt";
}
export function ObsMonitoring({ outputs }: { outputs?: ObsOutputs }) {
    const stats = outputs?.stats;
    return (
        <section aria-label="OBS-Livemonitoring" className="space-y-2 text-sm">
            <p>
                Streamlaufzeit:{" "}
                {outputs?.stream?.outputActive === true
                    ? clock(outputs.stream)
                    : "Unbekannt"}
            </p>
            <p>Aufnahme: {recordState(outputs?.record)}</p>
            <p>Replay Buffer: {outputState(outputs?.replay)}</p>
            <p>Virtuelle Kamera: {outputState(outputs?.camera)}</p>
            <div className="flex flex-wrap gap-x-4 gap-y-1 text-text-secondary">
                <p>CPU: {metric(stats?.cpuUsage, 1, " %")}</p>
                <p>FPS: {metric(stats?.activeFps, 1)}</p>
                <p>RAM: {metric(stats?.memoryUsage, 0, " MB")}</p>
                <p>
                    Render-Lag:{" "}
                    {frames(
                        stats?.renderSkippedFrames,
                        stats?.renderTotalFrames,
                    )}
                </p>
                <p>
                    Encoding-Lag:{" "}
                    {frames(
                        stats?.outputSkippedFrames,
                        stats?.outputTotalFrames,
                    )}
                </p>
            </div>
        </section>
    );
}
