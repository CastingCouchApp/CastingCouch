import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Card } from "../components/ui/card";
import { Button } from "../components/ui/button";
import {
  FALLBACK_POLL_MS,
  queryClient,
  queryKeys,
  tauriInvoke,
  type ObsSceneInfo,
  type ServiceStatus,
} from "../lib/api";
import type { AppSettings } from "../lib/app-settings";
import { useLiveServiceStatuses } from "../lib/live-events";

export const Route = createFileRoute("/services")({
  component: ServicesPage,
});

function invokeErrorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message;
  }
  if (typeof error === "string") {
    return error;
  }
  return "Unbekannter Fehler";
}

function ServicesPage() {
  useLiveServiceStatuses();
  const services = useQuery({
    queryKey: queryKeys.services,
    queryFn: () => tauriInvoke<ServiceStatus[]>("service_statuses"),
    refetchInterval: FALLBACK_POLL_MS,
  });
  const settings = useQuery({
    queryKey: queryKeys.settings,
    queryFn: () => tauriInvoke<AppSettings>("get_settings"),
  });

  const obs = (services.data ?? []).find((s) => s.id === "obs");
  const twitch = (services.data ?? []).find((s) => s.id === "twitch");
  const spotify = (services.data ?? []).find((s) => s.id === "spotify");
  const connected = obs?.state === "connected";
  const twitchConnected = twitch?.state === "connected";
  const twitchConnecting = twitch?.state === "connecting";
  const spotifyConnected = spotify?.state === "connected";
  const spotifyConnecting = spotify?.state === "connecting";

  const connectObs = useMutation({
    mutationFn: () => tauriInvoke<ServiceStatus>("connect_obs"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services });
      queryClient.invalidateQueries({ queryKey: queryKeys.obsScenes });
    },
  });

  const disconnectObs = useMutation({
    mutationFn: () => tauriInvoke<ServiceStatus>("disconnect_obs"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services });
      queryClient.setQueryData(queryKeys.obsScenes, []);
    },
  });

  const twitchLogin = useMutation({
    mutationFn: () => tauriInvoke<ServiceStatus>("twitch_login"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services });
    },
  });

  const twitchLogout = useMutation({
    mutationFn: () => tauriInvoke<ServiceStatus>("twitch_logout"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services });
    },
  });

  const spotifyLogin = useMutation({
    mutationFn: () => tauriInvoke<ServiceStatus>("spotify_login"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services });
      queryClient.invalidateQueries({ queryKey: queryKeys.nowPlaying });
    },
  });

  const spotifyLogout = useMutation({
    mutationFn: () => tauriInvoke<ServiceStatus>("spotify_logout"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services });
      queryClient.invalidateQueries({ queryKey: queryKeys.nowPlaying });
    },
  });

  const scenes = useQuery({
    queryKey: queryKeys.obsScenes,
    queryFn: () => tauriInvoke<ObsSceneInfo[]>("obs_scenes"),
    enabled: connected,
  });

  const setScene = useMutation({
    mutationFn: (scene: string) => tauriInvoke<void>("obs_set_scene", { scene }),
  });

  const autoConnect = {
    obs: settings.data?.Obs.AutoConnect === true,
    twitch: settings.data?.Twitch.AutoConnect === true,
    spotify: settings.data?.Spotify.AutoConnect === true,
  };

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Dienste</h1>
      <div className="grid gap-4 md:grid-cols-3">
        {(services.data ?? []).map((svc) => (
          <Card key={svc.id} className="space-y-3">
            <div>
              <div className="font-medium">{svc.name}</div>
              <div className="text-sm capitalize text-zinc-400">{svc.state}</div>
              {svc.detail ? (
                <div
                  className={
                    svc.state === "error"
                      ? "mt-1 whitespace-pre-wrap break-words text-xs text-red-400"
                      : "mt-1 whitespace-pre-wrap break-words text-xs text-zinc-500"
                  }
                >
                  {svc.detail}
                </div>
              ) : null}
              {svc.id === "obs" && autoConnect.obs && svc.state !== "connected" ? (
                <p className="mt-2 text-xs text-zinc-500">
                  Automatisch verbinden ist aktiv — Start versucht die Verbindung.
                </p>
              ) : null}
              {svc.id === "twitch" && autoConnect.twitch && svc.state !== "connected" ? (
                <p className="mt-2 text-xs text-zinc-500">
                  Automatisch verbinden ist aktiv — Start versucht die Verbindung.
                </p>
              ) : null}
              {svc.id === "spotify" && autoConnect.spotify && svc.state !== "connected" ? (
                <p className="mt-2 text-xs text-zinc-500">
                  Automatisch verbinden ist aktiv — Start versucht die Verbindung.
                </p>
              ) : null}
            </div>
            {svc.id === "obs" ? (
              <div className="space-y-3">
                <div className="flex flex-wrap gap-2">
                  <Button
                    onClick={() => connectObs.mutate()}
                    disabled={connectObs.isPending || svc.state === "connecting"}
                  >
                    Verbinden
                  </Button>
                  <Button
                    onClick={() => disconnectObs.mutate()}
                    disabled={disconnectObs.isPending || svc.state === "disconnected"}
                  >
                    Trennen
                  </Button>
                </div>
                {connectObs.isError ? (
                  <p className="whitespace-pre-wrap break-words text-xs text-red-400">
                    {invokeErrorMessage(connectObs.error)}
                  </p>
                ) : null}
                {disconnectObs.isError ? (
                  <p className="whitespace-pre-wrap break-words text-xs text-red-400">
                    {invokeErrorMessage(disconnectObs.error)}
                  </p>
                ) : null}
                {connected && (scenes.data?.length ?? 0) > 0 ? (
                  <div className="space-y-1">
                    <div className="text-xs text-zinc-500">Szenen</div>
                    <ul className="space-y-1">
                      {scenes.data!.map((scene) => (
                        <li key={`${scene.index}-${scene.name}`}>
                          <button
                            type="button"
                            className="w-full rounded px-2 py-1 text-left text-sm hover:bg-zinc-800"
                            onClick={() => setScene.mutate(scene.name)}
                            disabled={setScene.isPending}
                          >
                            {scene.name}
                          </button>
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </div>
            ) : svc.id === "twitch" ? (
              <div className="space-y-2">
                <div className="flex flex-wrap gap-2">
                  {twitchConnected ? (
                    <Button
                      onClick={() => twitchLogout.mutate()}
                      disabled={twitchLogout.isPending}
                    >
                      Abmelden
                    </Button>
                  ) : (
                    <Button
                      onClick={() => twitchLogin.mutate()}
                      disabled={
                        twitchLogin.isPending || twitchConnecting || svc.state === "connecting"
                      }
                    >
                      Anmelden
                    </Button>
                  )}
                </div>
                {twitchLogin.isError ? (
                  <p className="whitespace-pre-wrap break-words text-xs text-red-400">
                    {invokeErrorMessage(twitchLogin.error)}
                  </p>
                ) : null}
                {twitchLogout.isError ? (
                  <p className="whitespace-pre-wrap break-words text-xs text-red-400">
                    {invokeErrorMessage(twitchLogout.error)}
                  </p>
                ) : null}
              </div>
            ) : svc.id === "spotify" ? (
              <div className="space-y-2">
                <div className="flex flex-wrap gap-2">
                  {spotifyConnected ? (
                    <Button
                      onClick={() => spotifyLogout.mutate()}
                      disabled={spotifyLogout.isPending}
                    >
                      Abmelden
                    </Button>
                  ) : (
                    <Button
                      onClick={() => spotifyLogin.mutate()}
                      disabled={
                        spotifyLogin.isPending || spotifyConnecting || svc.state === "connecting"
                      }
                    >
                      Anmelden
                    </Button>
                  )}
                </div>
                {spotifyLogin.isError ? (
                  <p className="whitespace-pre-wrap break-words text-xs text-red-400">
                    {invokeErrorMessage(spotifyLogin.error)}
                  </p>
                ) : null}
                {spotifyLogout.isError ? (
                  <p className="whitespace-pre-wrap break-words text-xs text-red-400">
                    {invokeErrorMessage(spotifyLogout.error)}
                  </p>
                ) : null}
              </div>
            ) : null}
          </Card>
        ))}
      </div>
    </div>
  );
}
