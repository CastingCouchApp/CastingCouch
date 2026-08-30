import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Card } from "../components/ui/card";
import { queryKeys, tauriInvoke, type ServiceStatus } from "../lib/api";
import { useLiveServiceStatuses } from "../lib/live-events";

export const Route = createFileRoute("/")({
  component: DashboardPage,
});

function DashboardPage() {
  useLiveServiceStatuses();
  const services = useQuery({
    queryKey: queryKeys.services,
    queryFn: () => tauriInvoke<ServiceStatus[]>("service_statuses"),
    refetchInterval: 4000,
  });

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Dashboard</h1>
        <p className="text-sm text-zinc-400">Live-Status von OBS, Twitch und Spotify.</p>
      </header>
      <div className="grid gap-4 md:grid-cols-3">
        {(services.data ?? []).map((svc) => (
          <Card key={svc.id}>
            <div className="text-sm text-zinc-400">{svc.name}</div>
            <div className="mt-1 text-lg capitalize">{svc.state}</div>
            {svc.detail ? <div className="mt-2 truncate text-xs text-zinc-500">{svc.detail}</div> : null}
          </Card>
        ))}
      </div>
    </div>
  );
}
