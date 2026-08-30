import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import { queryClient, queryKeys, tauriInvoke, type AlertDefinition } from "../lib/api";

export const Route = createFileRoute("/alerts")({
  component: AlertsPage,
});

function AlertsPage() {
  const alerts = useQuery({
    queryKey: queryKeys.alerts,
    queryFn: () => tauriInvoke<AlertDefinition[]>("list_alerts"),
  });
  const upsert = useMutation({
    mutationFn: () =>
      tauriInvoke("upsert_alert", {
        alert: {
          id: crypto.randomUUID(),
          name: "Neues Alert",
          event_type: "channel.follow",
          enabled: true,
        },
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.alerts }),
  });

  const rows = alerts.data ?? [];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Alerts</h1>
        <Button onClick={() => upsert.mutate()}>Alert anlegen</Button>
      </div>
      <Card className="overflow-x-auto p-0">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-white/10 text-zinc-400">
            <tr>
              <th className="px-4 py-3 font-medium">Name</th>
              <th className="px-4 py-3 font-medium">Event</th>
              <th className="px-4 py-3 font-medium">Aktiv</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td className="px-4 py-6 text-zinc-500" colSpan={3}>
                  Noch keine Alerts.
                </td>
              </tr>
            ) : (
              rows.map((row) => (
                <tr key={row.id} className="border-b border-white/5">
                  <td className="px-4 py-3">{row.name}</td>
                  <td className="px-4 py-3">{row.event_type}</td>
                  <td className="px-4 py-3">{row.enabled ? "ja" : "nein"}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </Card>
    </div>
  );
}
