import type {AlertDefinition, UpdatePackage} from "./api";
export type ObsControl =
  | { action: "start_stream" }
  | { action: "stop_stream" }
  | { action: "start_record" }
  | { action: "stop_record" }
  | { action: "pause_record" }
  | { action: "resume_record" }
  | { action: "start_replay_buffer" }
  | { action: "stop_replay_buffer" }
  | { action: "save_replay_buffer" }
  | { action: "start_virtual_cam" }
  | { action: "stop_virtual_cam" }
  | { action: "set_profile"; profileName: string }
  | { action: "set_scene_collection"; sceneCollectionName: string }
  | { action: "set_transition"; transitionName: string }
  | { action: "set_transition_duration"; transitionDuration: number }
  | { action: "set_mute"; inputName: string; inputMuted: boolean }
  | { action: "set_volume"; inputName: string; inputVolumeDb: number }
  | { action: "set_monitor"; inputName: string; monitorType: string }
  | { action: "set_sync_offset"; inputName: string; inputAudioSyncOffset: number }
  | { action: "set_visibility"; sceneName: string; sceneItemId: number; sceneItemEnabled: boolean }
  | { action: "set_locked"; sceneName: string; sceneItemId: number; sceneItemLocked: boolean }
  | { action: "set_index"; sceneName: string; sceneItemId: number; sceneItemIndex: number }
  | { action: "set_transform"; sceneName: string; sceneItemId: number; sceneItemTransform: unknown }
  | { action: "set_filter"; sourceName: string; filterName: string; filterEnabled: boolean }
  | { action: "set_input_settings"; inputName: string; inputSettings: unknown };

export type ObsQuery =
  | { query: "transform"; sceneName: string; sceneItemId: number }
  | { query: "profiles" }
  | { query: "scene_collections" }
  | { query: "transitions" }
  | { query: "current_transition" }
  | { query: "inputs" }
  | { query: "scene_items"; sceneName: string }
  | { query: "group_items"; sceneName: string }
  | { query: "input_settings"; inputName: string }
  | { query: "mute"; inputName: string }
  | { query: "volume"; inputName: string }
  | { query: "audio_monitor"; inputName: string }
  | { query: "audio_sync_offset"; inputName: string }
  | { query: "filters"; sourceName: string }
  | { query: "filter_settings"; sourceName: string; filterName: string };

export type SpotifyAction =
  | { action: "play" }
  | { action: "pause" }
  | { action: "next" }
  | { action: "previous" }
  | { action: "volume"; percent: number }
  | { action: "seek"; positionMs: number }
  | { action: "shuffle"; enabled: boolean }
  | { action: "repeat"; mode: string }
  | { action: "transfer"; deviceId: string }
  | { action: "play_track"; uri: string }
  | { action: "play_playlist"; uri: string }
  | { action: "queue"; uri: string }
  | { action: "save_track"; id: string }
  | { action: "remove_saved_track"; id: string };

export type SpotifyQuery =
  | { query: "playback" }
  | { query: "devices" }
  | { query: "queue" }
  | { query: "recent" }
  | { query: "saved" }
  | { query: "playlists" }
  | { query: "playlist_tracks"; id: string }
  | { query: "search"; text: string };

export type TwitchAction =
  | { action: "channel"; title: string; categoryId: string }
  | { action: "send_chat"; message: string }
  | { action: "ban"; id: string; duration?: number | null; reason: string }
  | { action: "unban"; id: string }
  | { action: "delete_chat"; messageId?: string | null }
  | { action: "raid"; id: string }
  | { action: "cancel_raid" }
  | { action: "create_reward"; title: string; cost: number; prompt: string }
  | { action: "update_redemption"; rewardId: string; id: string; status: string }
  | { action: "create_poll"; title: string; choices: Array<string>; duration: number }
  | { action: "end_poll"; id: string; status: string }
  | { action: "create_prediction"; title: string; outcomes: Array<string>; window: number }
  | { action: "end_prediction"; id: string; status: string; winningOutcomeId?: string | null };

export type TwitchQuery =
  | { query: "channel" }
  | { query: "stream" }
  | { query: "followers" }
  | { query: "subscriptions" }
  | { query: "chatters" }
  | { query: "followed_channels" }
  | { query: "followed_streams" }
  | { query: "rewards" }
  | { query: "polls" }
  | { query: "predictions" }
  | { query: "search_categories"; text: string }
  | { query: "search_channels"; text: string }
  | { query: "redemptions"; rewardId: string };
// Generated from src-tauri/src/lib.rs. Run npm run contracts:generate.
export type CommandInvocation =
  | [command: "open_twitch_chat", args?: Record<string, never>]
  | [command: "twitch_action", args: { action: TwitchAction }]
  | [command: "twitch_query", args: { query: TwitchQuery; after?: string | null }]
  | [command: "chat_history", args?: Record<string, never>]
  | [command: "countdown_status", args?: Record<string, never>]
  | [command: "set_countdown", args: { seconds: number; label: string }]
  | [command: "spotify_action", args: { action: SpotifyAction }]
  | [command: "spotify_query", args: { query: SpotifyQuery; offset?: number | null }]
  | [command: "setup_overlay_source", args: { canvasId: string; sceneName: string; inputName: string }]
  | [command: "obs_query", args: { query: ObsQuery }]
  | [command: "obs_control", args: { control: ObsControl }]
  | [command: "obs_output_status", args?: Record<string, never>]
  | [command: "ytm_connect", args?: Record<string, never>]
  | [command: "ytm_disconnect", args?: Record<string, never>]
  | [command: "ytm_now_playing", args?: Record<string, never>]
  | [command: "ytm_command", args: { command: string }]
  | [command: "get_settings", args?: Record<string, never>]
  | [command: "save_settings", args: { settings: unknown; original: unknown; obsPassword?: string | null }]
  | [command: "list_canvases", args?: Record<string, never>]
  | [command: "create_canvas", args: { name: string }]
  | [command: "delete_canvas", args: { id: string }]
  | [command: "duplicate_canvas", args: { id: string }]
  | [command: "update_canvas", args: { id: string; name?: string | null; selected?: boolean | null }]
  | [command: "open_overlay_editor", args: { id: string; name: string; editorUrl: string }]
  | [command: "service_statuses", args?: Record<string, never>]
  | [command: "connect_obs", args?: Record<string, never>]
  | [command: "disconnect_obs", args?: Record<string, never>]
  | [command: "obs_scenes", args?: Record<string, never>]
  | [command: "obs_set_scene", args: { scene: string }]
  | [command: "obs_current_scene", args?: Record<string, never>]
  | [command: "set_obs_password", args: { password: string }]
  | [command: "obs_has_password", args?: Record<string, never>]
  | [command: "twitch_login", args?: Record<string, never>]
  | [command: "twitch_logout", args?: Record<string, never>]
  | [command: "spotify_login", args?: Record<string, never>]
  | [command: "spotify_logout", args?: Record<string, never>]
  | [command: "alert_install_sources", args: { alertType: string }]
  | [command: "alert_stop", args?: Record<string, never>]
  | [command: "alert_clear_queue", args?: Record<string, never>]
  | [command: "list_alerts", args?: Record<string, never>]
  | [command: "upsert_alert", args: { alert: AlertDefinition }]
  | [command: "delete_alert", args: { alertType: string }]
  | [command: "alert_runtime", args?: { enabled?: boolean | null; obsSceneName?: string | null }]
  | [command: "test_alert", args: { alertType: string; user?: string | null }]
  | [command: "now_playing", args?: Record<string, never>]
  | [command: "app_paths", args?: Record<string, never>]
  | [command: "overlay_health_url", args?: Record<string, never>]
  | [command: "app_version", args?: Record<string, never>]
  | [command: "check_updates", args?: Record<string, never>]
  | [command: "download_update", args: { package: UpdatePackage }]
  | [command: "apply_update", args?: Record<string, never>]
  | [command: "startup_error", args?: Record<string, never>]
  | [command: "overlay_runtime_status", args?: Record<string, never>];
