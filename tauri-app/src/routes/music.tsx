import { CommonMusicPlayer } from "../features/music/CommonMusicPlayer";
import { MusicOverlaySettings } from "../features/music/MusicOverlaySettings";
import { YouTubeMusicSetup } from "../features/music/YouTubeMusicSetup";
import { SpotifyLibrary } from "../features/music/SpotifyLibrary";
import { SceneMusic } from "../features/music/SceneMusic";
import { SavedMusicStates } from "../features/music/SavedMusicStates";
import { MusicStatistics } from "../features/music/MusicStatistics";
import { MusicAutomation } from "../features/music/MusicAutomation";
import { SpotifyDevices } from "../features/music/SpotifyDevices";
import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
    cloneSettings,
    musicProvider,
    type AppSettings,
} from "../lib/app-settings";
import { Card } from "../components/ui/card";
import { queryKeys, tauriInvoke } from "../lib/api";
export const Route = createFileRoute("/music")({ component: MusicPage });
function MusicPage() {
    const client = useQueryClient();
    const settings = useQuery({
        queryKey: queryKeys.settings,
        queryFn: () => tauriInvoke<AppSettings>("get_settings"),
    });
    const provider = useMutation({
        mutationFn: async (source: string) => {
            if (!settings.data) throw new Error("Einstellungen fehlen");
            const next = cloneSettings(settings.data);
            next.MusicPlayer = {
                ...next.MusicPlayer,
                Source: source,
                ProviderId: source,
            };
            return tauriInvoke<{ warnings: string[] }>("save_settings", {
                original: settings.data,
                settings: next,
            });
        },
        onSuccess: () =>
            client.invalidateQueries({ queryKey: queryKeys.settings }),
    });
    const error = provider.error;
    return (
        <div className="space-y-6">
            <h1 className="text-2xl font-semibold">Musik</h1>
            {error && (
                <p role="alert" className="text-red-400">
                    {String(error)}
                </p>
            )}
            <label className="block">
                Musikprovider{" "}
                <select
                    aria-label="Musikprovider"
                    className="bg-panel p-2"
                    disabled={!settings.data || provider.isPending}
                    value={musicProvider(settings.data?.MusicPlayer)}
                    onChange={(e) => provider.mutate(e.target.value)}
                >
                    <option value="spotify">Spotify</option>
                    <option value="ytmusic">YouTube Music</option>
                </select>
            </label>
            {provider.data?.warnings?.map((warning) => (
                <p role="alert" key={warning}>
                    {warning}
                </p>
            ))}
            <CommonMusicPlayer
                provider={musicProvider(settings.data?.MusicPlayer)}
                changingProvider={provider.isPending}
            />
            <MusicOverlaySettings />
            {musicProvider(settings.data?.MusicPlayer) === "spotify" ? (
                <>
                    <Card className="space-y-4">
                        <h2 className="text-lg font-semibold">
                            Spotify-Geräte
                        </h2>
                        <SpotifyDevices />
                    </Card>
                    <SpotifyLibrary />
                    <MusicAutomation />
                    <SceneMusic />
                    <SavedMusicStates />
                    <MusicStatistics />
                </>
            ) : (
                <YouTubeMusicSetup />
            )}
        </div>
    );
}
