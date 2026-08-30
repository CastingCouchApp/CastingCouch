import type { ReactNode } from "react";
import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Card } from "../components/ui/card";
import { cn } from "../lib/cn";
import {
  FALLBACK_POLL_MS,
  queryKeys,
  tauriInvoke,
  type NowPlaying,
  type ServiceStatus,
} from "../lib/api";
import { useLiveDashboard } from "../lib/live-events";

export const Route = createFileRoute("/")({
  component: DashboardPage,
});

function connectionLabel(state: ServiceStatus["state"]): string {
  switch (state) {
    case "connected":
      return "Verbunden";
    case "connecting":
      return "Verbinden …";
    case "error":
      return "Fehler";
    default:
      return "Getrennt";
  }
}

function pickStatus(
  list: ServiceStatus[] | undefined,
  id: string,
  name: string,
): ServiceStatus {
  return list?.find((item) => item.id === id) ?? { id, name, state: "disconnected", detail: "" };
}

function DashboardPage() {
  useLiveDashboard();
  const services = useQuery({
    queryKey: queryKeys.services,
    queryFn: () => tauriInvoke<ServiceStatus[]>("service_statuses"),
    refetchInterval: FALLBACK_POLL_MS,
  });

  const obs = pickStatus(services.data, "obs", "OBS");
  const twitch = pickStatus(services.data, "twitch", "Twitch");
  const spotify = pickStatus(services.data, "spotify", "Spotify");

  const scene = useQuery({
    queryKey: queryKeys.obsCurrentScene,
    queryFn: () => tauriInvoke<string | null>("obs_current_scene"),
    refetchInterval: FALLBACK_POLL_MS,
  });

  const nowPlaying = useQuery({
    queryKey: queryKeys.nowPlaying,
    queryFn: () => tauriInvoke<NowPlaying>("now_playing"),
    refetchInterval: FALLBACK_POLL_MS,
  });

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Dashboard</h1>
        <p className="text-sm text-zinc-400">Live-Status von OBS, Twitch und Spotify.</p>
      </header>
      <div className="grid gap-4 md:grid-cols-3">
        <ServiceCard status={obs}>
          <ObsBody status={obs} scene={scene.data} />
        </ServiceCard>
        <ServiceCard status={twitch}>
          <TwitchBody status={twitch} />
        </ServiceCard>
        <ServiceCard status={spotify}>
          <SpotifyBody status={spotify} playing={nowPlaying.data} />
        </ServiceCard>
      </div>
    </div>
  );
}

function ServiceCard({
  status,
  children,
}: {
  status: ServiceStatus;
  children: ReactNode;
}) {
  return (
    <Card>
      <div className="text-sm text-zinc-400">{status.name}</div>
      <div
        className={cn(
          "mt-1 text-lg",
          status.state === "error" && "text-red-400",
          status.state === "connected" && "text-emerald-400",
          status.state === "connecting" && "text-amber-300",
          status.state === "disconnected" && "text-zinc-300",
        )}
      >
        {connectionLabel(status.state)}
      </div>
      {children}
    </Card>
  );
}

function ErrorDetail({ status }: { status: ServiceStatus }) {
  if (status.state !== "error" || !status.detail) {
    return null;
  }
  return (
    <div className="mt-2 whitespace-pre-wrap break-words text-xs text-red-400">{status.detail}</div>
  );
}

function ObsBody({ status, scene }: { status: ServiceStatus; scene: string | null | undefined }) {
  const label =
    status.state === "connected" && scene && scene.trim().length > 0 ? scene : "Keine Szene";
  return (
    <>
      <div className="mt-2 truncate text-sm text-zinc-200">{label}</div>
      <ErrorDetail status={status} />
    </>
  );
}

function TwitchBody({ status }: { status: ServiceStatus }) {
  const login =
    status.state === "connected" && status.detail.trim().length > 0
      ? status.detail
      : status.state === "connecting" && status.detail.trim().length > 0
        ? status.detail
        : status.state === "error"
          ? null
          : "Nicht angemeldet";
  return (
    <>
      {login ? <div className="mt-2 truncate text-sm text-zinc-200">{login}</div> : null}
      <ErrorDetail status={status} />
    </>
  );
}

function SpotifyBody({
  status,
  playing,
}: {
  status: ServiceStatus;
  playing: NowPlaying | undefined;
}) {
  const hasTrack = Boolean(playing?.title);
  return (
    <>
      {hasTrack ? (
        <div className="mt-2 space-y-1">
          <div className="truncate text-sm text-zinc-200">{playing!.title}</div>
          {playing!.artist ? (
            <div className="truncate text-xs text-zinc-400">{playing!.artist}</div>
          ) : null}
          {playing!.album ? (
            <div className="truncate text-xs text-zinc-500">{playing!.album}</div>
          ) : null}
          <div className="text-xs text-zinc-500">{playing!.is_playing ? "Spielt" : "Pausiert"}</div>
        </div>
      ) : (
        <div className="mt-2 text-sm text-zinc-200">Keine Wiedergabe</div>
      )}
      <ErrorDetail status={status} />
    </>
  );
}
