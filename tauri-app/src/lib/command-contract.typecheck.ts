import { tauriInvoke } from "./api";
// Compiled by tsc; these calls are never executed. Stale argument names must fail compilation.
export function commandContractTypeAssertions() {
    void tauriInvoke("start_twitch_raid", {login:"target"});
    // @ts-expect-error A login is required for fresh raid preflight.
    void tauriInvoke("start_twitch_raid", {id:"target"});
    // @ts-expect-error Reviewed original settings are required for raid list edits.
    void tauriInvoke("save_twitch_raid_settings", {channels:["target"],selected:"target"});
    // @ts-expect-error Goal drafts must include all typed nested fields.
    void tauriInvoke("save_twitch_goals", {
        draft: { overlayScene: "Goals" },
        original: {},
    });
    void tauriInvoke("music_state_action", {
        action: { action: "restore", group: "Intro", fadeSeconds: 3 },
    });
    const badRestore = {
        action: "restore" as const,
        group: "Intro",
        fade_seconds: 3,
    };
    // @ts-expect-error Rust restore requires camelCase fadeSeconds.
    void tauriInvoke("music_state_action", { action: badRestore });
    // @ts-expect-error Reviewed original is required for selective restoration.
    void tauriInvoke("music_state_action", {
        action: { action: "backup_restore", id: "backup.json", options: {} },
    });
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
