import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import type { ServiceStatus, WorkflowRunResponse } from "../../lib/api";

const invokeMock = vi.fn();

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
  };
});

function renderWorkflow() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/workflow"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

function sidecarStatus(state: ServiceStatus["state"], detail = ""): ServiceStatus {
  return { id: "sidecar", name: "Sidecar", state, detail };
}

const stubResponse: WorkflowRunResponse = {
  ok: false,
  message: "Run-of-Show noch nicht im Sidecar",
};

describe("Workflow page", () => {
  beforeEach(() => {
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "sidecar_status") {
        return sidecarStatus("disconnected");
      }
      if (cmd === "sidecar_workflow_run") {
        return stubResponse;
      }
      return undefined;
    });
  });

  it("disables the run button and skips invoke when sidecar is down", async () => {
    renderWorkflow();
    expect(await screen.findByRole("heading", { name: "Workflow" })).toBeInTheDocument();
    expect(await screen.findByText("Getrennt")).toBeInTheDocument();
    expect(screen.getByText(/Sidecar ist nicht verbunden/)).toBeInTheDocument();

    const button = screen.getByRole("button", { name: "Schritt ausführen" });
    expect(button).toBeDisabled();
    expect(invokeMock).not.toHaveBeenCalledWith("sidecar_workflow_run", expect.anything());
  });

  it("runs workflow.prepare by default when sidecar is connected", async () => {
    invokeMock.mockImplementation(async (cmd: string, args?: Record<string, unknown>) => {
      if (cmd === "sidecar_status") {
        return sidecarStatus("connected", "http://127.0.0.1:18765");
      }
      if (cmd === "sidecar_workflow_run") {
        expect(args).toEqual({ command: "workflow.prepare" });
        return stubResponse;
      }
      return undefined;
    });

    const user = userEvent.setup();
    renderWorkflow();
    expect(await screen.findByText("Verbunden")).toBeInTheDocument();

    const button = screen.getByRole("button", { name: "Schritt ausführen" });
    expect(button).toBeEnabled();
    await user.click(button);

    await waitFor(() => {
      expect(invokeMock).toHaveBeenCalledWith("sidecar_workflow_run", {
        command: "workflow.prepare",
      });
    });
    expect(await screen.findByText("Run-of-Show noch nicht im Sidecar")).toBeInTheDocument();
  });

  it("sends the selected workflow command", async () => {
    invokeMock.mockImplementation(async (cmd: string) => {
      if (cmd === "sidecar_status") {
        return sidecarStatus("connected", "http://127.0.0.1:18765");
      }
      if (cmd === "sidecar_workflow_run") {
        return stubResponse;
      }
      return undefined;
    });

    const user = userEvent.setup();
    renderWorkflow();
    await screen.findByText("Verbunden");

    await user.selectOptions(screen.getByLabelText("Schritt"), "workflow.live");
    await user.click(screen.getByRole("button", { name: "Schritt ausführen" }));

    await waitFor(() => {
      expect(invokeMock).toHaveBeenCalledWith("sidecar_workflow_run", {
        command: "workflow.live",
      });
    });
  });
});
