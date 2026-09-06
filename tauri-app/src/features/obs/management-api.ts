import { useQuery } from "@tanstack/react-query";
import { tauriInvoke } from "../../lib/api";
import type {
    ObsQuery,
    ObsControl as Control,
} from "../../lib/command-contract";
export type {
    ObsQuery,
    ObsControl as Control,
} from "../../lib/command-contract";
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
