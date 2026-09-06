import { tauriInvoke } from "./api";
// Compiled by tsc; these calls are never executed. Stale argument names must fail compilation.
export function commandContractTypeAssertions() {
    const oldQuery = { query: "scene_items" as const, scene_name: "Live" };
    // @ts-expect-error Nested domain actions use the actual Rust contract.
    void tauriInvoke("obs_query", { query: oldQuery });
    // @ts-expect-error Domain action argument required.
    void tauriInvoke("spotify_action", { action: { action: "volume" } });

    void tauriInvoke("open_overlay_editor", {
        id: "default",
        name: "Canvas",
        editorUrl: "http://127.0.0.1:8765/editor/default",
    });
    // @ts-expect-error Rust accepts camelCase only.
    void tauriInvoke("open_overlay_editor", { editor_url: "url" });
    // @ts-expect-error Required argument must be present.
    void tauriInvoke("set_countdown", { label: "Countdown" });
    // @ts-expect-error Commands removed from the native host cannot be invoked.
    void tauriInvoke("workflow_start");
    // @ts-expect-error Incorrect primitive types cannot cross IPC.
    void tauriInvoke("set_countdown", { seconds: "60", label: "Countdown" });
}
