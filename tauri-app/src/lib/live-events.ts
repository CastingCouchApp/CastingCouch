import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import {
  listenServiceStatus,
  mergeServiceStatus,
  queryKeys,
  type ServiceStatus,
} from "./api";

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
