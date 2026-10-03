import { expect, it } from "vitest";
import { tauriInvoke } from "../../lib/api";
it("labels read-only demo state and rejects native assistant mutations", async () => {
    const status = await tauriInvoke<any>("stream_end_status");
    expect(status.active).toBe(false);
    expect(status.status).toContain("Demo");
    const settings = await tauriInvoke<any>("stream_end_snapshot");
    expect(settings.outgoingRaid.available).toBe(false);
    expect(settings.warnings[0]).toContain("Desktop-App");
    await expect(
        tauriInvoke("start_stream_end", { planned: false }),
    ).rejects.toThrow("Demo-Modus");
    await expect(
        tauriInvoke("stream_end_control", { action: "abort" }),
    ).rejects.toThrow("Demo-Modus");
});
