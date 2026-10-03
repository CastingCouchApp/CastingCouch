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
export function useObsQuery<T>(
    query: ObsQuery,
    enabled = true,
    refetchInterval?: number,
) {
    return useQuery({
        queryKey: ["obs-management", query],
        queryFn: () => tauriInvoke<T>("obs_query", { query }),
        enabled,
        refetchInterval,
        retry: false,
    });
}
export type Item = {
    sourceName: string;
    sourceType?: string;
    inputKind?: string;
    sceneItemId: number;
    sceneItemIndex: number;
    sceneItemEnabled: boolean;
    sceneItemLocked: boolean;
    isGroup?: boolean;
    sceneItemTransform: Record<string, number>;
};
