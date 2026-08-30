import { QueryClient } from "@tanstack/react-query";
import { invoke } from "@tauri-apps/api/core";
import { cloneSettings, defaultAppSettings, type AppSettings } from "./app-settings";

export type { AppSettings } from "./app-settings";
export { applyThemeId, cloneSettings, defaultAppSettings, THEME_CATALOG } from "./app-settings";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5_000,
      retry: 1,
    },
  },
});

/** Fallback poll when Tauri events are missed; live updates go through listen → setQueryData. */
export const FALLBACK_POLL_MS = 15_000;

export const queryKeys = {
  settings: ["settings"] as const,
  canvases: ["canvases"] as const,
  services: ["services"] as const,
  obsScenes: ["obs-scenes"] as const,
  obsCurrentScene: ["obs-current-scene"] as const,
  alerts: ["alerts"] as const,
  alertRuntime: ["alert-runtime"] as const,
  nowPlaying: ["now-playing"] as const,
  paths: ["paths"] as const,
  overlayHealthUrl: ["overlay-health-url"] as const,
  overlayHealth: ["overlay-health"] as const,
  appVersion: ["app-version"] as const,
  updates: ["updates"] as const,
};

export type CanvasDto = {
  id: string;
  name: string;
  editor_url: string;
  view_url: string;
};

export type ServiceStatus = {
  id: string;
  name: string;
  state: "disconnected" | "connecting" | "connected" | "error";
  detail: string;
};

export type NowPlaying = {
  title: string;
  artist: string;
  album: string;
  is_playing: boolean;
};

export const EMPTY_NOW_PLAYING: NowPlaying = {
  title: "",
  artist: "",
  album: "",
  is_playing: false,
};

export type ObsSceneInfo = {
  name: string;
  index: number;
};

export type AlertDefinition = {
  type: string;
  enabled: boolean;
  text_template: string;
  media_path: string;
  sound_path: string;
  duration_seconds: number;
  priority: number;
  font_face: string;
  font_size: number;
  font_color: string;
  animation: string;
  x: number;
  y: number;
  width: number;
  height: number;
  volume_percent: number;
  sound_start_seconds: number;
  sound_end_seconds: number;
  audio_output_device_id: string;
};

export type AlertRuntime = {
  pending_count: number;
  enabled: boolean;
  obs_scene_name: string;
};

export type AppVersionInfo = {
  version: string;
  channel: string;
};

export type UpdatePackage = {
  product_id: string;
  version: string;
  channel: string;
  download_uri: string;
  sha256: string;
  size: number;
  release_notes: string;
  package_file_name: string;
};

export type UpdateCheckResult = {
  update_available: boolean;
  current_version: string;
  package: UpdatePackage | null;
  detail: string;
};

export const EMPTY_UPDATE_CHECK: UpdateCheckResult = {
  update_available: false,
  current_version: "8.0.0-beta.1",
  package: null,
  detail: "Bereit.",
};

let mockSettings = defaultAppSettings();

export async function tauriInvoke<T>(cmd: string, args?: Record<string, unknown>): Promise<T> {
  if (typeof window !== "undefined" && "__TAURI_INTERNALS__" in window) {
    return invoke<T>(cmd, args);
  }
  return mockInvoke<T>(cmd, args);
}

function mockInvoke<T>(cmd: string, args?: Record<string, unknown>): T {
  switch (cmd) {
    case "list_canvases":
      return [
        {
          id: "default",
          name: "Canvas",
          editor_url: "http://127.0.0.1:8765/editor/default",
          view_url: "http://127.0.0.1:8765/view/default",
        },
      ] as T;
    case "service_statuses":
      return [
        { id: "obs", name: "OBS", state: "disconnected", detail: "" },
        { id: "twitch", name: "Twitch", state: "disconnected", detail: "" },
        { id: "spotify", name: "Spotify", state: "disconnected", detail: "" },
        { id: "sidecar", name: "Sidecar", state: "disconnected", detail: "" },
      ] as T;
    case "sidecar_status":
      return { id: "sidecar", name: "Sidecar", state: "disconnected", detail: "" } as T;
    case "sidecar_ytm_now_playing":
      return {
        provider: "ytmusic",
        connected: false,
        isPlaying: false,
        title: "",
        artist: "",
        album: "",
        statusText: "Nicht verbunden",
      } as T;
    case "connect_obs":
      return { id: "obs", name: "OBS", state: "connected", detail: "ws://127.0.0.1:4455" } as T;
    case "disconnect_obs":
      return { id: "obs", name: "OBS", state: "disconnected", detail: "" } as T;
    case "twitch_login":
      return {
        id: "twitch",
        name: "Twitch",
        state: "connecting",
        detail: "Code: ABCD-EFGH",
      } as T;
    case "twitch_logout":
      return { id: "twitch", name: "Twitch", state: "disconnected", detail: "" } as T;
    case "spotify_login":
      return {
        id: "spotify",
        name: "Spotify",
        state: "connecting",
        detail: "Warte auf Spotify-Anmeldung …",
      } as T;
    case "spotify_logout":
      return { id: "spotify", name: "Spotify", state: "disconnected", detail: "" } as T;
    case "now_playing":
      return { title: "", artist: "", album: "", is_playing: false } as T;
    case "obs_current_scene":
      return null as T;
    case "obs_scenes":
      return [
        { name: "Start", index: 0 },
        { name: "Live", index: 1 },
      ] as T;
    case "obs_set_scene":
      return undefined as T;
    case "obs_has_password":
      return false as T;
    case "set_obs_password":
      return undefined as T;
    case "list_alerts":
      return [
        {
          type: "Follow",
          enabled: true,
          text_template: "{user} folgt jetzt!",
          media_path: "",
          sound_path: "",
          duration_seconds: 8,
          priority: 100,
          font_face: "Segoe UI",
          font_size: 44,
          font_color: "#FFFFFF",
          animation: "Fade",
          x: 510,
          y: 690,
          width: 900,
          height: 260,
          volume_percent: 100,
          sound_start_seconds: 0,
          sound_end_seconds: 0,
          audio_output_device_id: "",
        },
      ] as T;
    case "alert_runtime":
      return { pending_count: 0, enabled: true, obs_scene_name: "_alerts" } as T;
    case "upsert_alert":
      return args?.alert as T;
    case "delete_alert":
      return undefined as T;
    case "test_alert":
      return 1 as T;
    case "get_settings":
      return cloneSettings(mockSettings) as T;
    case "save_settings":
      if (args?.settings) {
        mockSettings = cloneSettings(args.settings as AppSettings);
      }
      return undefined as T;
    case "app_paths":
      return "CreatorControlSuite" as T;
    case "create_canvas":
      return {
        id: "new",
        name: String(args?.name ?? "Canvas"),
        editor_url: "http://127.0.0.1:8765/editor/new",
        view_url: "http://127.0.0.1:8765/view/new",
      } as T;
    case "duplicate_canvas":
      return {
        id: "canvas-kopie",
        name: "Canvas (Kopie)",
        editor_url: "http://127.0.0.1:8765/editor/canvas-kopie",
        view_url: "http://127.0.0.1:8765/view/canvas-kopie",
      } as T;
    case "delete_canvas":
      return undefined as T;
    case "overlay_health_url":
      return "http://127.0.0.1:8765/health" as T;
    case "open_overlay_editor":
      return undefined as T;
    case "app_version":
      return { version: "8.0.0-beta.1", channel: "Alpha" } as T;
    case "check_updates":
      return {
        update_available: false,
        current_version: "8.0.0-beta.1",
        package: null,
        detail: "Aktuelle Version 8.0.0-beta.1 ist aktuell (Alpha).",
      } as T;
    case "download_update":
      return "CreatorControlSuite/Downloads/pkg.zip" as T;
    case "apply_update":
      return "Installation folgt in Phase 5." as T;
    default:
      return undefined as T;
  }
}

export function mergeServiceStatus(
  list: ServiceStatus[] | undefined,
  next: ServiceStatus,
): ServiceStatus[] {
  const current = list ?? [];
  const index = current.findIndex((item) => item.id === next.id);
  if (index < 0) {
    return [...current, next];
  }
  const copy = current.slice();
  copy[index] = next;
  return copy;
}

async function listenIfTauri<T>(
  event: string,
  onPayload: (payload: T) => void,
): Promise<() => void> {
  if (typeof window !== "undefined" && "__TAURI_INTERNALS__" in window) {
    const { listen } = await import("@tauri-apps/api/event");
    return listen<T>(event, (e) => {
      onPayload(e.payload);
    });
  }
  return () => {};
}

export async function listenServiceStatus(
  onStatus: (status: ServiceStatus) => void,
): Promise<() => void> {
  return listenIfTauri<ServiceStatus>("service-status", onStatus);
}

export async function listenObsScene(
  onScene: (scene: string) => void,
): Promise<() => void> {
  return listenIfTauri<{ scene: string }>("obs-scene", (payload) => {
    onScene(payload.scene);
  });
}

export async function listenNowPlaying(
  onPlaying: (playing: NowPlaying) => void,
): Promise<() => void> {
  return listenIfTauri<NowPlaying>("now-playing", onPlaying);
}
