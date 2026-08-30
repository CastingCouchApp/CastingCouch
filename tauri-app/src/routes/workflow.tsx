import { useState } from "react";
import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import { cn } from "../lib/cn";
import {
  DEFAULT_WORKFLOW_COMMAND,
  FALLBACK_POLL_MS,
  queryKeys,
  tauriInvoke,
  WORKFLOW_COMMANDS,
  type ServiceStatus,
  type WorkflowRunResponse,
} from "../lib/api";

export const Route = createFileRoute("/workflow")({
  component: WorkflowPage,
});

function connectionLabel(state: ServiceStatus["state"] | undefined): string {
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

function WorkflowPage() {
  const [command, setCommand] = useState<string>(DEFAULT_WORKFLOW_COMMAND);
  const sidecar = useQuery({
    queryKey: queryKeys.sidecarStatus,
    queryFn: () => tauriInvoke<ServiceStatus>("sidecar_status"),
    refetchInterval: FALLBACK_POLL_MS,
  });
  const connected = sidecar.data?.state === "connected";

  const run = useMutation({
    mutationFn: (next: string) =>
      tauriInvoke<WorkflowRunResponse>("sidecar_workflow_run", { command: next }),
  });

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-semibold">Workflow</h1>
        <p className="text-sm text-zinc-400">Einzelner Run-of-Show-Schritt über den Sidecar.</p>
      </header>
      <Card className="max-w-xl space-y-4">
        <div>
          <div className="text-sm text-zinc-400">Sidecar</div>
          <div
            className={cn(
              "mt-1 text-lg",
              sidecar.data?.state === "error" && "text-red-400",
              sidecar.data?.state === "connected" && "text-emerald-400",
              sidecar.data?.state === "connecting" && "text-amber-300",
              (!sidecar.data || sidecar.data.state === "disconnected") && "text-zinc-300",
            )}
          >
            {connectionLabel(sidecar.data?.state)}
          </div>
          {sidecar.data?.detail ? (
            <div
              className={
                sidecar.data.state === "error"
                  ? "mt-1 whitespace-pre-wrap break-words text-xs text-red-400"
                  : "mt-1 whitespace-pre-wrap break-words text-xs text-zinc-500"
              }
            >
              {sidecar.data.detail}
            </div>
          ) : null}
          {!connected ? (
            <p className="mt-2 text-sm text-zinc-200">
              Sidecar ist nicht verbunden. Schritt ausführen ist deaktiviert.
            </p>
          ) : null}
        </div>
        <div className="space-y-2">
          <label htmlFor="workflow-step" className="block text-sm text-zinc-400">
            Schritt
          </label>
          <select
            id="workflow-step"
            className="w-full rounded-md border border-border bg-input px-3 py-1.5 text-sm text-text outline-none focus:border-brand"
            value={command}
            onChange={(event) => setCommand(event.target.value)}
            disabled={!connected}
          >
            {WORKFLOW_COMMANDS.map((item) => (
              <option key={item.value} value={item.value}>
                {item.label}
              </option>
            ))}
          </select>
        </div>
        <Button
          onClick={() => run.mutate(command)}
          disabled={!connected || run.isPending}
        >
          Schritt ausführen
        </Button>
        {run.data?.message ? (
          <p className="whitespace-pre-wrap break-words text-sm text-zinc-200">{run.data.message}</p>
        ) : null}
        {run.isError ? (
          <p className="whitespace-pre-wrap break-words text-sm text-red-400">
            {run.error instanceof Error ? run.error.message : String(run.error)}
          </p>
        ) : null}
      </Card>
    </div>
  );
}
