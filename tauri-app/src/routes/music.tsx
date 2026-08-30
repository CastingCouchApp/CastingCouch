import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Card } from "../components/ui/card";
import {
  FALLBACK_POLL_MS,
  queryKeys,
  tauriInvoke,
  type NowPlaying,
  type ServiceStatus,
  type YtmNowPlaying,
} from "../lib/api";

export const Route = createFileRoute("/music")({
  component: MusicPage,
});

function MusicPage() {
  const sidecar = useQuery({
    queryKey: queryKeys.sidecarStatus,
    queryFn: () => tauriInvoke<ServiceStatus>("sidecar_status"),
    refetchInterval: FALLBACK_POLL_MS,
  });
  const nowPlaying = useQuery({
    queryKey: queryKeys.nowPlaying,
    queryFn: () => tauriInvoke<NowPlaying>("now_playing"),
    refetchInterval: FALLBACK_POLL_MS,
  });
  const sidecarHealthy = sidecar.data?.state === "connected";
  const ytm = useQuery({
    queryKey: queryKeys.ytmNowPlaying,
    queryFn: () => tauriInvoke<YtmNowPlaying>("sidecar_ytm_now_playing"),
    enabled: sidecarHealthy,
    refetchInterval: FALLBACK_POLL_MS,
  });

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Musik</h1>
        <p className="text-sm text-zinc-400">Spotify nativ, YouTube Music über den Sidecar.</p>
      </header>
      <div className="grid gap-4 md:grid-cols-2">
        <SpotifyCard playing={nowPlaying.data} />
        {sidecarHealthy ? (
          <YtmCard playing={ytm.data} />
        ) : (
          <Card>
            <div className="text-sm text-zinc-400">YouTube Music</div>
            <p className="mt-2 text-sm text-zinc-200">YouTube Music braucht den Sidecar.</p>
          </Card>
        )}
      </div>
    </div>
  );
}

function SpotifyCard({ playing }: { playing: NowPlaying | undefined }) {
  return (
    <Card>
      <div className="text-sm text-zinc-400">Spotify</div>
      <TrackBody
        title={playing?.title}
        artist={playing?.artist}
        album={playing?.album}
        isPlaying={playing?.is_playing}
      />
    </Card>
  );
}

function YtmCard({ playing }: { playing: YtmNowPlaying | undefined }) {
  return (
    <Card>
      <div className="text-sm text-zinc-400">YouTube Music</div>
      <TrackBody
        title={playing?.title}
        artist={playing?.artist}
        album={playing?.album}
        isPlaying={playing?.isPlaying}
        fallback={playing?.statusText}
      />
    </Card>
  );
}

function TrackBody({
  title,
  artist,
  album,
  isPlaying,
  fallback,
}: {
  title: string | undefined;
  artist: string | undefined;
  album: string | undefined;
  isPlaying: boolean | undefined;
  fallback?: string;
}) {
  if (title) {
    return (
      <div className="mt-2 space-y-1">
        <div className="truncate text-sm text-zinc-200">{title}</div>
        {artist ? <div className="truncate text-xs text-zinc-400">{artist}</div> : null}
        {album ? <div className="truncate text-xs text-zinc-500">{album}</div> : null}
        <div className="text-xs text-zinc-500">{isPlaying ? "Spielt" : "Pausiert"}</div>
      </div>
    );
  }
  return (
    <div className="mt-2 text-sm text-zinc-200">{fallback?.trim() || "Keine Wiedergabe"}</div>
  );
}
