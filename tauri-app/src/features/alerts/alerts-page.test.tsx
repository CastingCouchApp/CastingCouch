import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import type { AlertDefinition, AlertRuntime } from "../../lib/api";

const invokeMock = vi.fn();

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
  };
});

function sampleAlert(type: string, enabled: boolean): AlertDefinition {
  return {
    type,
    enabled,
    text_template: `{user} ${type}`,
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
  };
}

let alerts: AlertDefinition[];
let runtime: AlertRuntime;

function renderAlerts() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/alerts"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("Alerts library page", () => {
  beforeEach(() => {
    alerts = [sampleAlert("Cheer", false), sampleAlert("Follow", true)];
    runtime = { pending_count: 2, enabled: true, obs_scene_name: "_alerts" };
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string, args?: Record<string, unknown>) => {
      if (cmd === "list_alerts") {
        return alerts;
      }
      if (cmd === "alert_runtime") {
        if (typeof args?.enabled === "boolean") {
          runtime = { ...runtime, enabled: args.enabled };
        }
        if (typeof args?.obs_scene_name === "string") {
          runtime = { ...runtime, obs_scene_name: args.obs_scene_name };
        }
        return runtime;
      }
      if (cmd === "upsert_alert") {
        return args?.alert;
      }
      if (cmd === "delete_alert") {
        return undefined;
      }
      if (cmd === "test_alert") {
        return 1;
      }
      return undefined;
    });
  });

  it("renders library rows and runtime status", async () => {
    renderAlerts();
    expect(await screen.findByRole("heading", { name: "Alerts" }, { timeout: 5000 })).toBeInTheDocument();
    expect(await screen.findByText("Follow")).toBeInTheDocument();
    expect(screen.getByText("Cheer")).toBeInTheDocument();
    expect(screen.getByText("Queue: 2")).toBeInTheDocument();
    expect(screen.getByLabelText("OBS-Szene")).toHaveValue("_alerts");
    expect(screen.getByRole("checkbox", { name: "Alerts aktiv" })).toBeChecked();
  });

  it("toggles a disabled alert via upsert_alert", async () => {
    const user = userEvent.setup();
    renderAlerts();
    const cheerRow = (await screen.findByText("Cheer")).closest("tr");
    expect(cheerRow).toBeTruthy();
    await user.click(within(cheerRow as HTMLElement).getByRole("button", { name: "Aktivieren" }));
    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith(
        "upsert_alert",
        expect.objectContaining({
          alert: expect.objectContaining({ type: "Cheer", enabled: true }),
        }),
      ),
    );
  });

  it("creates an alert", async () => {
    const user = userEvent.setup();
    renderAlerts();
    await screen.findByRole("heading", { name: "Alerts" });
    await user.click(screen.getByRole("button", { name: "Alert anlegen" }));
    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith(
        "upsert_alert",
        expect.objectContaining({
          alert: expect.objectContaining({ type: "", enabled: true }),
        }),
      ),
    );
  });

  it("deletes after confirmation", async () => {
    const user = userEvent.setup();
    vi.spyOn(window, "confirm").mockReturnValue(true);
    renderAlerts();
    const followRow = (await screen.findByText("Follow")).closest("tr");
    await user.click(within(followRow as HTMLElement).getByRole("button", { name: "Löschen" }));
    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith("delete_alert", { alert_type: "Follow" }),
    );
  });

  it("fires test_alert for a row", async () => {
    const user = userEvent.setup();
    renderAlerts();
    const followRow = (await screen.findByText("Follow")).closest("tr");
    await user.click(within(followRow as HTMLElement).getByRole("button", { name: "Testen" }));
    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith("test_alert", {
        alert_type: "Follow",
        user: "Test",
      }),
    );
  });

  it("saves OBS scene name", async () => {
    const user = userEvent.setup();
    renderAlerts();
    const input = await screen.findByLabelText("OBS-Szene");
    await user.clear(input);
    await user.type(input, "overlay");
    await user.click(screen.getByRole("button", { name: "Szene speichern" }));
    await waitFor(() =>
      expect(invokeMock).toHaveBeenCalledWith(
        "alert_runtime",
        expect.objectContaining({ obs_scene_name: "overlay" }),
      ),
    );
  });
});
