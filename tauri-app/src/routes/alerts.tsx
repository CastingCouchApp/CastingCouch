import { createFileRoute } from "@tanstack/react-router";
import { flexRender } from "@tanstack/react-table";
import { getCoreRowModel, useLegacyTable, type LegacyColumnDef } from "@tanstack/react-table/legacy";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import { Input } from "../components/ui/input";
import {
  queryClient,
  queryKeys,
  tauriInvoke,
  type AlertDefinition,
  type AlertRuntime,
} from "../lib/api";

export const Route = createFileRoute("/alerts")({
  component: AlertsPage,
});

function AlertsPage() {
  const alerts = useQuery({
    queryKey: queryKeys.alerts,
    queryFn: () => tauriInvoke<AlertDefinition[]>("list_alerts"),
  });
  const runtime = useQuery({
    queryKey: queryKeys.alertRuntime,
    queryFn: () => tauriInvoke<AlertRuntime>("alert_runtime"),
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.alerts });
    void queryClient.invalidateQueries({ queryKey: queryKeys.alertRuntime });
  };

  const upsert = useMutation({
    mutationFn: (alert: AlertDefinition) => tauriInvoke<AlertDefinition>("upsert_alert", { alert }),
    onSuccess: invalidate,
  });
  const remove = useMutation({
    mutationFn: (alertType: string) => tauriInvoke("delete_alert", { alert_type: alertType }),
    onSuccess: invalidate,
  });
  const test = useMutation({
    mutationFn: (alertType: string) =>
      tauriInvoke("test_alert", { alert_type: alertType, user: "Test" }),
    onSuccess: invalidate,
  });
  const patchRuntime = useMutation({
    mutationFn: (patch: { enabled?: boolean; obs_scene_name?: string }) =>
      tauriInvoke<AlertRuntime>("alert_runtime", patch),
    onSuccess: invalidate,
  });

  const [sceneName, setSceneName] = useState<string | null>(null);
  const sceneValue = sceneName ?? runtime.data?.obs_scene_name ?? "_alerts";

  const columns = useMemo<LegacyColumnDef<AlertDefinition>[]>(
    () => [
      { accessorKey: "type", header: "Typ" },
      {
        accessorKey: "enabled",
        header: "Aktiv",
        cell: ({ row }) => (row.original.enabled ? "ja" : "nein"),
      },
      {
        id: "actions",
        header: "",
        cell: ({ row }) => {
          const alert = row.original;
          return (
            <div className="flex flex-wrap gap-2">
              <Button
                variant="ghost"
                onClick={() => upsert.mutate({ ...alert, enabled: !alert.enabled })}
              >
                {alert.enabled ? "Deaktivieren" : "Aktivieren"}
              </Button>
              <Button variant="ghost" onClick={() => test.mutate(alert.type)}>
                Testen
              </Button>
              <Button
                variant="ghost"
                onClick={() => upsert.mutate({ ...alert, type: `${alert.type} Kopie` })}
              >
                Duplizieren
              </Button>
              <Button
                variant="danger"
                onClick={() => {
                  if (window.confirm(`Alert „${alert.type}“ löschen?`)) {
                    remove.mutate(alert.type);
                  }
                }}
              >
                Löschen
              </Button>
            </div>
          );
        },
      },
    ],
    [remove, test, upsert],
  );

  const table = useLegacyTable({
    data: alerts.data ?? [],
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Alerts</h1>
        <Button
          onClick={() =>
            upsert.mutate({
              type: "",
              enabled: true,
              text_template: "",
              media_path: "",
              sound_path: "",
              duration_seconds: 8,
              priority: 100,
              font_face: "Segoe UI",
              font_size: 44,
              font_color: "#FFFFFF",
              animation: "Fade",
              x: 510,
              y: 690,
              width: 900,
              height: 260,
              volume_percent: 100,
              sound_start_seconds: 0,
              sound_end_seconds: 0,
              audio_output_device_id: "",
            })
          }
        >
          Alert anlegen
        </Button>
      </div>

      <Card className="flex flex-wrap items-center gap-4">
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={runtime.data?.enabled ?? true}
            onChange={(e) => patchRuntime.mutate({ enabled: e.target.checked })}
          />
          Alerts aktiv
        </label>
        <span className="text-sm text-zinc-400">Queue: {runtime.data?.pending_count ?? 0}</span>
        <label className="flex min-w-48 flex-1 items-center gap-2 text-sm">
          OBS-Szene
          <Input
            aria-label="OBS-Szene"
            value={sceneValue}
            onChange={(e) => setSceneName(e.target.value)}
          />
        </label>
        <Button variant="ghost" onClick={() => patchRuntime.mutate({ obs_scene_name: sceneValue })}>
          Szene speichern
        </Button>
      </Card>

      <Card className="overflow-x-auto p-0">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-white/10 text-zinc-400">
            {table.getHeaderGroups().map((group) => (
              <tr key={group.id}>
                {group.headers.map((header) => (
                  <th key={header.id} className="px-4 py-3 font-medium">
                    {header.isPlaceholder
                      ? null
                      : flexRender(header.column.columnDef.header, header.getContext())}
                  </th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.length === 0 ? (
              <tr>
                <td className="px-4 py-6 text-zinc-500" colSpan={columns.length}>
                  Noch keine Alerts.
                </td>
              </tr>
            ) : (
              table.getRowModel().rows.map((row) => (
                <tr key={row.id} className="border-b border-white/5">
                  {row.getVisibleCells().map((cell) => (
                    <td key={cell.id} className="px-4 py-3">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </Card>
    </div>
  );
}
