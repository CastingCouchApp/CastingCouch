import { QueryClient } from "@tanstack/react-query";
import { invoke } from "@tauri-apps/api/core";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5_000,
      retry: 1,
    },
  },
});

export const queryKeys = {
  settings: ["settings"] as const,
  canvases: ["canvases"] as const,
  services: ["services"] as const,
  obsScenes: ["obs-scenes"] as const,
  alerts: ["alerts"] as const,
  nowPlaying: ["now-playing"] as const,
  paths: ["paths"] as const,
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

export type ObsSceneInfo = {
  name: string;
  index: number;
};

export type AlertDefinition = {
  id: string;
  name: string;
  event_type: string;
  enabled: boolean;
};

export type AppSettings = {
  SchemaVersion: number;
  General: { Language: string; ThemeId: string };
  Obs: { Host: string; Port: number; AutoConnect: boolean };
  Twitch: { ChannelName: string; ClientId: string; AutoConnect: boolean };
  Overlay: { WebServerPort: number; SelectedCanvasId: string };
  Branding: { DisplayName: string; AccentColor: string };
};

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
      ] as T;
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
      return [] as T;
    case "get_settings":
      return {
        SchemaVersion: 2,
        General: { Language: "de-DE", ThemeId: "classic" },
        Obs: { Host: "127.0.0.1", Port: 4455, AutoConnect: true },
        Twitch: { ChannelName: "", ClientId: "", AutoConnect: true },
        Overlay: { WebServerPort: 8765, SelectedCanvasId: "default" },
        Branding: { DisplayName: "Mein Stream", AccentColor: "#FF8C00" },
      } as T;
    case "app_paths":
      return "CreatorControlSuite" as T;
    case "create_canvas":
      return {
        id: "new",
        name: String(args?.name ?? "Canvas"),
        editor_url: "http://127.0.0.1:8765/editor/new",
        view_url: "http://127.0.0.1:8765/view/new",
      } as T;
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

export async function listenServiceStatus(
  onStatus: (status: ServiceStatus) => void,
): Promise<() => void> {
  if (typeof window !== "undefined" && "__TAURI_INTERNALS__" in window) {
    const { listen } = await import("@tauri-apps/api/event");
    return listen<ServiceStatus>("service-status", (event) => {
      onStatus(event.payload);
    });
  }
  return () => {};
}
