import { useQuery } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
export type ObsQuery =
    | { query: "transform"; sceneName: string; sceneItemId: number }
    | {
          query:
              | "profiles"
              | "scene_collections"
              | "transitions"
              | "current_transition"
              | "inputs";
      }
    | { query: "scene_items" | "group_items"; sceneName: string }
    | {
          query:
              | "input_settings"
              | "mute"
              | "volume"
              | "audio_monitor"
              | "audio_sync_offset";
          inputName: string;
      }
    | { query: "filters"; sourceName: string };
export type Control =
    | { action: "set_profile"; profileName: string }
    | { action: "set_scene_collection"; sceneCollectionName: string }
    | { action: "set_transition"; transitionName: string }
    | { action: "set_transition_duration"; transitionDuration: number }
    | { action: "set_mute"; inputName: string; inputMuted: boolean }
    | { action: "set_volume"; inputName: string; inputVolumeDb: number }
    | { action: "set_monitor"; inputName: string; monitorType: string }
    | {
          action: "set_sync_offset";
          inputName: string;
          inputAudioSyncOffset: number;
      }
    | {
          action: "set_visibility";
          sceneName: string;
          sceneItemId: number;
          sceneItemEnabled: boolean;
      }
    | {
          action: "set_locked";
          sceneName: string;
          sceneItemId: number;
          sceneItemLocked: boolean;
      }
    | {
          action: "set_index";
          sceneName: string;
          sceneItemId: number;
          sceneItemIndex: number;
      }
    | {
          action: "set_transform";
          sceneName: string;
          sceneItemId: number;
          sceneItemTransform: Record<string, number>;
      }
    | {
          action: "set_filter";
          sourceName: string;
          filterName: string;
          filterEnabled: boolean;
      }
    | {
          action: "set_input_settings";
          inputName: string;
          inputSettings: Record<string, unknown>;
      };
export type Apply = (control: Control) => Promise<unknown>;
export function useObsQuery<T>(query: ObsQuery, enabled = true) {
    return useQuery({
        queryKey: ["obs-management", query],
        queryFn: () => tauriInvoke<T>("obs_query", { query }),
        enabled,
        retry: false,
    });
}
export type Item = {
    sourceName: string;
    sceneItemId: number;
    sceneItemIndex: number;
    sceneItemEnabled: boolean;
    sceneItemLocked: boolean;
    isGroup?: boolean;
    sceneItemTransform: Record<string, number>;
};
