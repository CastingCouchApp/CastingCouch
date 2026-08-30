import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import { cloneSettings, defaultAppSettings, type AppSettings } from "../../lib/app-settings";
import "../../styles.css";

const invokeMock = vi.fn();

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      invokeMock(cmd, args) as Promise<T>,
  };
});

function wpfLikeSettings(): AppSettings {
  const settings = cloneSettings(defaultAppSettings());
  settings.General.ThemeId = "classic";
  settings.Overlay.Canvases = [
    { Id: "default", Name: "Canvas" },
    { Id: "brb", Name: "BRB" },
  ];
  settings.Overlay.SelectedCanvasId = "brb";
  settings.Alerts.Definitions.Follow.TextTemplate = "{user} folgt jetzt! (custom)";
  Object.assign(settings.Spotify, {
    PreferredDeviceId: "device-x",
    StartPlaylistUri: "spotify:playlist:x",
  });
  settings.StreamerBot = { Host: "127.0.0.1", Port: 8080 };
  settings.Workflow = { LastStep: "prepare" };
  return settings;
}

function renderSettings() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/settings"] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("Settings route", () => {
  let stored: AppSettings;

  beforeEach(() => {
    stored = wpfLikeSettings();
    document.documentElement.removeAttribute("data-theme");
    invokeMock.mockReset();
    invokeMock.mockImplementation(async (cmd: string, args?: Record<string, unknown>) => {
      if (cmd === "get_settings") {
        return cloneSettings(stored);
      }
      if (cmd === "save_settings") {
        stored = cloneSettings(args?.settings as AppSettings);
        return undefined;
      }
      if (cmd === "obs_has_password") {
        return false;
      }
      if (cmd === "set_obs_password") {
        return undefined;
      }
      return undefined;
    });
  });

  it("renders General, OBS, Twitch, Spotify, Overlay and Branding sections", async () => {
    renderSettings();
    expect(await screen.findByRole("heading", { name: "Einstellungen" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Allgemein" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "OBS" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Twitch" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Spotify" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Overlay" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Branding" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "OBS automatisch verbinden" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Twitch automatisch verbinden" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "Spotify automatisch verbinden" })).toBeChecked();
  });

  it("saves theme change without dropping WPF extra fields", async () => {
    const user = userEvent.setup();
    renderSettings();
    const theme = await screen.findByLabelText("Theme");
    await user.selectOptions(theme, "neon-night-market");
    await user.click(screen.getByRole("button", { name: "Speichern" }));

    await waitFor(() => {
      const saveCall = invokeMock.mock.calls.find((call) => call[0] === "save_settings");
      expect(saveCall).toBeTruthy();
      const payload = saveCall![1]?.settings as AppSettings;
      expect(payload.General.ThemeId).toBe("neon-night-market");
      expect(payload.Overlay.Canvases).toEqual([
        { Id: "default", Name: "Canvas" },
        { Id: "brb", Name: "BRB" },
      ]);
      expect(payload.Overlay.SelectedCanvasId).toBe("brb");
      expect(payload.Alerts.Definitions.Follow.TextTemplate).toBe("{user} folgt jetzt! (custom)");
      expect(payload.Spotify).toMatchObject({
        PreferredDeviceId: "device-x",
        StartPlaylistUri: "spotify:playlist:x",
      });
      expect(payload.StreamerBot).toEqual({ Host: "127.0.0.1", Port: 8080 });
      expect(payload.Workflow).toEqual({ LastStep: "prepare" });
    });
    expect(document.documentElement.dataset.theme).toBe("neon-night-market");
  });

  it("applies loaded ThemeId to html[data-theme]", async () => {
    stored.General.ThemeId = "arctic-glass-lab";
    renderSettings();
    await screen.findByRole("heading", { name: "Einstellungen" });
    await waitFor(() => {
      expect(document.documentElement.dataset.theme).toBe("arctic-glass-lab");
    });
  });

  it("applies theme tokens immediately without save", async () => {
    const user = userEvent.setup();
    renderSettings();
    const theme = await screen.findByLabelText("Theme");
    await user.selectOptions(theme, "comic-sans-extravaganza");
    expect(document.documentElement.dataset.theme).toBe("comic-sans-extravaganza");
    const windowToken = getComputedStyle(document.documentElement)
      .getPropertyValue("--color-window")
      .trim();
    const brandToken = getComputedStyle(document.documentElement)
      .getPropertyValue("--color-brand")
      .trim();
    if (windowToken) {
      expect(windowToken).toBe("#0a1a4a");
    }
    if (brandToken) {
      expect(brandToken).toBe("#ffe600");
    }
  });

  it("falls unknown ThemeId back to classic", async () => {
    stored.General.ThemeId = "not-a-theme";
    renderSettings();
    await screen.findByRole("heading", { name: "Einstellungen" });
    await waitFor(() => {
      expect(document.documentElement.dataset.theme).toBe("classic");
    });
  });
});
