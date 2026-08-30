import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
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
  const create = useMutation({
    mutationFn: () => tauriInvoke<CanvasDto>("create_canvas", { name: "Neues Canvas" }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.canvases }),
  });
  const remove = useMutation({
    mutationFn: (id: string) => tauriInvoke("delete_canvas", { id }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.canvases }),
  });
  const duplicate = useMutation({
    mutationFn: (id: string) => tauriInvoke<CanvasDto>("duplicate_canvas", { id }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.canvases }),
  });

  const rows = canvases.data ?? [];

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Overlay</h1>
        <Button onClick={() => create.mutate()}>Canvas anlegen</Button>
      </div>
      <Card className="overflow-x-auto p-0">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-white/10 text-zinc-400">
            <tr>
              <th className="px-4 py-3 font-medium">Name</th>
              <th className="px-4 py-3 font-medium">Id</th>
              <th className="px-4 py-3 font-medium">View-URL</th>
              <th className="px-4 py-3 font-medium" />
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id} className="border-b border-white/5">
                <td className="px-4 py-3">{row.name}</td>
                <td className="px-4 py-3">{row.id}</td>
                <td className="px-4 py-3">
                  <code className="text-xs text-zinc-400">{row.view_url}</code>
                </td>
                <td className="px-4 py-3">
                  <div className="flex gap-2">
                    <Button variant="ghost" onClick={() => window.open(row.editor_url, "_blank")}>
                      Editor
                    </Button>
                    <Button variant="ghost" onClick={() => duplicate.mutate(row.id)}>
                      Duplizieren
                    </Button>
                    <Button variant="danger" onClick={() => remove.mutate(row.id)}>
                      Löschen
                    </Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </Card>
    </div>
  );
}
