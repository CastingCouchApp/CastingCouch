import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Card } from "../components/ui/card";
import { queryKeys, tauriInvoke, type AppVersionInfo } from "../lib/api";

export const Route = createFileRoute("/about")({
  component: AboutPage,
});

function AboutPage() {
  const version = useQuery({
    queryKey: queryKeys.appVersion,
    queryFn: () => tauriInvoke<AppVersionInfo>("app_version"),
  });
  const dataPath = useQuery({
    queryKey: queryKeys.paths,
    queryFn: () => tauriInvoke<string>("app_paths"),
  });
  const healthUrl = useQuery({
    queryKey: queryKeys.overlayHealthUrl,
    queryFn: () => tauriInvoke<string>("overlay_health_url"),
  });

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold">Über CastingCouch</h1>
      <Card className="space-y-3">
        <p className="text-sm">
          Version: <strong>{version.data?.version ?? "…"}</strong>
          {version.data?.channel ? (
            <span className="text-muted"> ({version.data.channel})</span>
          ) : null}
        </p>
        <p className="text-sm">
          Datenpfad: <code data-testid="data-path">{dataPath.data ?? "…"}</code>
        </p>
        <p className="text-sm">
          Overlay-Health:{" "}
          {healthUrl.data ? (
            <a
              className="text-brand underline"
              href={healthUrl.data}
              data-testid="overlay-health-link"
            >
              {healthUrl.data}
            </a>
          ) : (
            "…"
          )}
        </p>
        <p className="text-sm text-muted">
          Tauri-Port der Creator Control Suite. Overlay-Editor und OBS-Browser-Sources nutzen
          weiterhin Loopback <code>http://127.0.0.1:8765</code>.
        </p>
      </Card>
    </div>
  );
}
