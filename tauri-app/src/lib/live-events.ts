import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  EMPTY_NOW_PLAYING,
  listenNowPlaying,
  listenObsScene,
  listenServiceStatus,
  mergeServiceStatus,
  queryKeys,
  type NowPlaying,
  type ServiceStatus,
} from "./api";

function applyServiceStatusSideEffects(
  client: ReturnType<typeof useQueryClient>,
  status: ServiceStatus,
) {
  if (status.id === "obs" && (status.state === "disconnected" || status.state === "error")) {
    client.setQueryData(queryKeys.obsCurrentScene, null);
  }
  if (
    status.id === "spotify" &&
    (status.state === "disconnected" || status.state === "error")
  ) {
    client.setQueryData(queryKeys.nowPlaying, EMPTY_NOW_PLAYING);
  }
}

/** Subscribe to Tauri `service-status` events and patch the services query cache. */
export function useLiveServiceStatuses() {
  const client = useQueryClient();
  useEffect(() => {
    let cancelled = false;
    let unlisten: (() => void) | undefined;
    listenServiceStatus((status) => {
      client.setQueryData<ServiceStatus[]>(queryKeys.services, (prev) =>
        mergeServiceStatus(prev, status),
      );
      applyServiceStatusSideEffects(client, status);
    }).then((fn) => {
      if (cancelled) {
        fn();
        return;
      }
      unlisten = fn;
    });
    return () => {
      cancelled = true;
      unlisten?.();
    };
  }, [client]);
}

/** Subscribe to Tauri `obs-scene` events and patch the current-scene query cache. */
export function useLiveObsScene() {
  const client = useQueryClient();
  useEffect(() => {
    let cancelled = false;
    let unlisten: (() => void) | undefined;
    listenObsScene((scene) => {
      client.setQueryData<string | null>(queryKeys.obsCurrentScene, scene);
      void client.cancelQueries({ queryKey: queryKeys.obsCurrentScene });
    }).then((fn) => {
      if (cancelled) {
        fn();
        return;
      }
      unlisten = fn;
    });
    return () => {
      cancelled = true;
      unlisten?.();
    };
  }, [client]);
}

/** Subscribe to Tauri `now-playing` events and patch the now-playing query cache. */
export function useLiveNowPlaying() {
  const client = useQueryClient();
  useEffect(() => {
    let cancelled = false;
    let unlisten: (() => void) | undefined;
    listenNowPlaying((playing: NowPlaying) => {
      client.setQueryData(queryKeys.nowPlaying, playing);
      void client.cancelQueries({ queryKey: queryKeys.nowPlaying });
    }).then((fn) => {
      if (cancelled) {
        fn();
        return;
      }
      unlisten = fn;
    });
    return () => {
      cancelled = true;
      unlisten?.();
    };
  }, [client]);
}

export function useLiveDashboard() {
  useLiveServiceStatuses();
  useLiveObsScene();
  useLiveNowPlaying();
}
