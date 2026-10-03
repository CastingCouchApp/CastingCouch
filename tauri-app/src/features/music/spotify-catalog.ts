import type { AppSettings } from "../../lib/app-settings";

export type SpotifyPlaylist = {
    id: string;
    uri: string;
    name: string;
    owner?: { display_name?: string; id?: string };
    images?: { url: string }[];
    items?: { total: number };
    tracks?: { total: number };
};
export const spotifyPlaylistsKey = ["spotify-playlists"] as const;
export function stringList(value: unknown): string[] {
    return Array.isArray(value)
        ? value.filter((entry): entry is string => typeof entry === "string")
        : [];
}
// WPF: favorites first, then recent; URI matching/deduplication is case-insensitive.
export function quickPlaylistChoices(
    catalog: SpotifyPlaylist[],
    settings: AppSettings | null | undefined,
): SpotifyPlaylist[] {
    const uris = new Set(
        [
            ...stringList(settings?.Spotify.FavoritePlaylistUris),
            ...stringList(settings?.Spotify.RecentPlaylistUris),
        ].map((uri) => uri.toLowerCase()),
    );
    return [...uris].flatMap((uri) => {
        const playlist = catalog.find(
            (entry) => entry.uri?.toLowerCase() === uri,
        );
        return playlist ? [playlist] : [];
    });
}
