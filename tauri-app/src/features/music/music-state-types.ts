import type { MusicStateAction } from "../../lib/command-contract";
import { tauriInvoke } from "../../lib/api";

export type RestoreOptions = {
    Entries: boolean;
    Favorites: boolean;
    Notes: boolean;
    Counters: boolean;
    Filters: boolean;
    MergeEntries: boolean;
};
export type RestoreProfile = RestoreOptions & {
    Name: string;
    IsBuiltIn: boolean;
    [key: string]: unknown;
};
export type MusicHistory = {
    SavedCount: number;
    RestoredCount: number;
    DiscardedCount: number;
    CleanupCount: number;
    Entries: string[];
    FavoriteEntries: string[];
    Notes: Record<string, string>;
    SearchText: string;
    ActionFilterIndex: number;
    SortIndex: number;
    FavoritesOnly: boolean;
};
export type MusicStateSnapshot = {
    states: Record<
        string,
        {
            ContextUri: string;
            Track: {
                Name?: string;
                Artist?: string;
                DurationMs?: number;
            } | null;
            ProgressMs: number;
            VolumePercent: number;
            ShuffleEnabled: boolean;
            RepeatMode: string;
            WasPlaying: boolean;
            SavedAtUtc: string;
        }
    >;
    history: MusicHistory;
    visibleHistory: string[];
    profiles: RestoreProfile[];
    backups: Array<{ id: string; at: string; bytes: number }>;
    health: {
        detail: string;
        error: string | null;
        lastRecovery: string | null;
    };
};
export type BackupPreview = {
    original: MusicHistory;
    backup: MusicHistory;
    added: string[];
    removed: string[];
    unchanged: number;
};
export type ProfilePreview = {
    original: unknown;
    profiles: Array<{ status: string; profile: RestoreProfile }>;
};
export const defaultRestore: RestoreOptions = {
    Entries: true,
    Favorites: false,
    Notes: false,
    Counters: false,
    Filters: false,
    MergeEntries: true,
};
export const optionLabels: Record<keyof RestoreOptions, string> = {
    Entries: "Verlauf wiederherstellen",
    Favorites: "Favoriten wiederherstellen",
    Notes: "Notizen wiederherstellen",
    Counters: "Zähler wiederherstellen",
    Filters: "Filter wiederherstellen",
    MergeEntries: "Verlauf zusammenführen",
};
export const musicStateKey = ["music-state-snapshot"] as const;
export const selectClass =
    "rounded border border-border bg-input p-2 text-text";
export function musicStateAction<T = unknown>(
    action: MusicStateAction,
): Promise<T> {
    return tauriInvoke<T>("music_state_action", { action });
}
export type RunOperation = (operation: () => Promise<void>) => void;
