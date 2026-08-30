import { createFileRoute } from "@tanstack/react-router";
import { flexRender } from "@tanstack/react-table";
import { getCoreRowModel, useLegacyTable, type LegacyColumnDef } from "@tanstack/react-table/legacy";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import { queryClient, queryKeys, tauriInvoke, type CanvasDto } from "../lib/api";

export const Route = createFileRoute("/overlay")({
  component: OverlayPage,
});

function OverlayPage() {
  const canvases = useQuery({
    queryKey: queryKeys.canvases,
    queryFn: () => tauriInvoke<CanvasDto[]>("list_canvases"),
  });
  const healthUrl = useQuery({
    queryKey: queryKeys.overlayHealthUrl,
    queryFn: () => tauriInvoke<string>("overlay_health_url"),
  });
  const health = useQuery({
    queryKey: [...queryKeys.overlayHealth, healthUrl.data],
    enabled: Boolean(healthUrl.data),
    retry: false,
    queryFn: async () => {
      const res = await fetch(healthUrl.data!);
      if (!res.ok) {
        throw new Error(`HTTP ${res.status}`);
      }
      return (await res.json()) as { ok?: boolean };
    },
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.canvases });
  };

  const create = useMutation({
    mutationFn: (name: string) => tauriInvoke<CanvasDto>("create_canvas", { name }),
    onSuccess: invalidate,
  });
  const remove = useMutation({
    mutationFn: (id: string) => tauriInvoke("delete_canvas", { id }),
    onSuccess: invalidate,
  });
  const duplicate = useMutation({
    mutationFn: (id: string) => tauriInvoke<CanvasDto>("duplicate_canvas", { id }),
    onSuccess: invalidate,
  });
  const openEditor = useMutation({
    mutationFn: (row: CanvasDto) =>
      tauriInvoke("open_overlay_editor", {
        id: row.id,
        name: row.name,
        editor_url: row.editor_url,
      }),
  });

  const rows = canvases.data ?? [];
  const canDelete = rows.length > 1;

  const columns = useMemo<LegacyColumnDef<CanvasDto>[]>(
    () => [
      { accessorKey: "name", header: "Name" },
      {
        accessorKey: "view_url",
        header: "View-URL",
        cell: ({ row }) => (
          <code className="text-xs text-zinc-400">{row.original.view_url}</code>
        ),
      },
      {
        id: "actions",
        header: "Aktionen",
        cell: ({ row }) => {
          const canvas = row.original;
          return (
            <div className="flex flex-wrap gap-2">
              <Button variant="ghost" onClick={() => openEditor.mutate(canvas)}>
                Editor öffnen
              </Button>
              <Button
                variant="ghost"
                onClick={() => {
                  void navigator.clipboard.writeText(canvas.view_url);
                }}
              >
                URL kopieren
              </Button>
              <Button variant="ghost" onClick={() => duplicate.mutate(canvas.id)}>
                Duplizieren
              </Button>
              <Button
                variant="danger"
                disabled={!canDelete}
                onClick={() => {
                  if (window.confirm(`Canvas „${canvas.name}“ löschen?`)) {
                    remove.mutate(canvas.id);
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
    [canDelete, duplicate, openEditor, remove],
  );

  const table = useLegacyTable({
    data: rows,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  const healthLabel = health.isSuccess && health.data.ok
    ? "Overlay-Server: erreichbar"
    : health.isError || (health.isSuccess && !health.data.ok)
      ? "Overlay-Server: nicht erreichbar"
      : "Overlay-Server: prüfe …";

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-2xl font-semibold">Overlay</h1>
        <Button
          onClick={() => {
            const name = window.prompt("Canvas-Name", "Neues Canvas");
            if (!name?.trim()) {
              return;
            }
            create.mutate(name.trim());
          }}
        >
          Canvas anlegen
        </Button>
      </div>
      <p className="text-sm text-zinc-400" data-testid="overlay-health">
        {healthLabel}
      </p>
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
                  Noch keine Canvases.
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
