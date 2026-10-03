import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RouterProvider, createMemoryHistory, createRouter } from "@tanstack/react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { routeTree } from "../../routeTree.gen";
import { cloneSettings, defaultAppSettings, type AppSettings } from "../../lib/app-settings";
import "../../styles.css";

const invokeMock = vi.fn();
const openMock = vi.fn();
vi.mock("@tauri-apps/plugin-dialog", () => ({ open: (...args: unknown[]) => openMock(...args), save: vi.fn() }));

vi.mock("../../lib/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../lib/api")>();
  return {
    ...actual,
    tauriInvoke: <T,>(cmd: string, args?: Record<string, unknown>) =>
      (cmd === "startup_error" ? Promise.resolve(null) : cmd === "overlay_runtime_status" ? Promise.resolve({ running: true, error: null }) : invokeMock(cmd, args)) as Promise<T>,
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
    openMock.mockReset();
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

  it("edits chat appearance and retains unedited legacy fields in the save contract", async () => {
    Object.assign(stored.Overlay.Chat, { FontFamily: "Arial", FutureStyle: { keep: true } });
    const user = userEvent.setup();
    renderSettings();
    await user.selectOptions(await screen.findByLabelText("Chat-Hintergrund"), "Color");
    for (const [label, value] of [["Chat-Hintergrundfarbe", "#123456"], ["Chat-Schrift", "Verdana"], ["Chat-Schriftgröße (px)", "24"], ["Chat-Innenabstand (px)", "16"], ["Chat-Eckenradius (px)", "8"], ["Chat-Zeilenabstand (px)", "10"], ["Chat-Hintergrunddeckkraft (%)", "30"]]) {
      const input = screen.getByLabelText(label);
      await user.clear(input);
      await user.type(input, value);
    }
    await user.click(screen.getByRole("button", { name: "Speichern" }));
    await waitFor(() => expect(stored.Overlay.Chat).toMatchObject({
      BackgroundType: "Color", BackgroundColor: "#123456", FontFamily: "Verdana",
      FontSizePx: 24, PaddingPx: 16, BorderRadiusPx: 8, GapPx: 10, BackgroundOpacity: 0.3,
      FutureStyle: { keep: true }, MaxBufferedMessages: 100,
    }));
  });

  it("selects a chat background image, preserves cancellation and exposes dialog errors", async () => {
    const user = userEvent.setup();
    renderSettings();
    const button = await screen.findByRole("button", { name: "Chat-Hintergrundbild auswählen" });
    openMock.mockResolvedValueOnce("C:\\images\\chat.png");
    await user.click(button);
    await waitFor(() => expect(screen.getByLabelText("Chat-Hintergrundbild")).toHaveValue("C:\\images\\chat.png"));
    expect(screen.getByLabelText("Chat-Hintergrund")).toHaveValue("Image");
    openMock.mockResolvedValueOnce(null);
    await user.click(button);
    expect(screen.getByLabelText("Chat-Hintergrundbild")).toHaveValue("C:\\images\\chat.png");
    openMock.mockRejectedValueOnce(new Error("Dialog unavailable"));
    await user.click(button);
    expect(await screen.findByText("Dialog unavailable")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Speichern" }));
    await waitFor(() => expect(stored.Overlay.Chat).toMatchObject({ BackgroundType: "Image", BackgroundImagePath: "C:\\images\\chat.png" }));
  });

  it("enables and persists each native third-party chat emote provider", async () => {
    const user = userEvent.setup();
    renderSettings();
    for (const label of ["BTTV-Emotes", "FrankerFaceZ-Emotes", "7TV-Emotes"]) {
      const checkbox = await screen.findByLabelText(label);
      expect(checkbox).toBeEnabled();
      await user.click(checkbox);
    }
    await user.click(screen.getByRole("button", { name: "Speichern" }));
    await waitFor(() => expect(stored.Overlay.Chat.EnableBttv).toBe(false));
    expect(stored.Overlay.Chat.EnableFfz).toBe(false);
    expect(stored.Overlay.Chat.EnableSevenTv).toBe(false);
  });

  it("preserves edits made while the image dialog is pending", async () => {
    let resolve!: (value: string) => void;
    openMock.mockImplementationOnce(() => new Promise(r => { resolve = r; }));
    const user = userEvent.setup();
    renderSettings();
    await user.click(await screen.findByRole("button", { name: "Chat-Hintergrundbild auswählen" }));
    await user.click(screen.getByLabelText("BTTV-Emotes"));
    resolve("C:\\images\\chat.png");
    await waitFor(() => expect(screen.getByLabelText("Chat-Hintergrundbild")).toHaveValue("C:\\images\\chat.png"));
    expect(screen.getByLabelText("BTTV-Emotes")).not.toBeChecked();
    await user.click(screen.getByRole("button", { name: "Speichern" }));
    await waitFor(() => expect(stored.Overlay.Chat.EnableBttv).toBe(false));
  });

  it("displays normalized legacy values without blocking an unrelated lossless save", async () => {
    Object.assign(stored.Overlay.Chat, { FontSizePx: 1, PaddingPx: 999, BackgroundOpacity: 2, FontFamily: null });
    const user = userEvent.setup();
    renderSettings();
    expect(await screen.findByLabelText("Chat-Schriftgröße (px)")).toHaveValue(8);
    expect(screen.getByLabelText("Chat-Innenabstand (px)")).toHaveValue(120);
    expect(screen.getByLabelText("Chat-Hintergrunddeckkraft (%)")).toHaveValue(100);
    await user.selectOptions(screen.getByLabelText("Theme"), "neon-night-market");
    await user.click(screen.getByRole("button", { name: "Speichern" }));
    await waitFor(() => expect(stored.General.ThemeId).toBe("neon-night-market"));
    expect(stored.Overlay.Chat).toMatchObject({ FontSizePx: 1, PaddingPx: 999, BackgroundOpacity: 2, FontFamily: null });
  });

  it("refreshes settings form and theme after applying a profile", async () => {
    const baseImplementation = invokeMock.getMockImplementation()!;
    invokeMock.mockImplementation(async (cmd: string, args?: Record<string, unknown>) => {
      if (cmd === "list_profiles") return { profiles: [{ id: "profile", name: "Studio", description: "", updatedAt: "2026-01-01" }], warnings: [] };
      if (cmd === "apply_profile") {
        stored.General.ThemeId = "arctic-glass-lab";
        stored.Twitch.ChannelName = "profile_channel";
        return { saved: true, warnings: ["Connection retry needed"] };
      }
      return baseImplementation(cmd, args);
    });
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    const user = userEvent.setup();
    renderSettings();
    await user.type((await screen.findAllByLabelText("Kanalname"))[0], "unsaved");
    await user.click(screen.getByRole("button", { name: "Profil anwenden" }));
    await waitFor(() => expect(screen.getAllByLabelText("Kanalname")[0]).toHaveValue("profile_channel"));
    expect(screen.getByLabelText("Theme")).toHaveValue("arctic-glass-lab");
    expect(document.documentElement.dataset.theme).toBe("arctic-glass-lab");
    expect(screen.getByText("Connection retry needed")).toBeInTheDocument();
    confirm.mockRestore();
  });

    it("marks pending desktop options as unavailable", async () => {
        renderSettings();
        expect(
            await screen.findByRole("checkbox", {
                name: /Mit Windows starten/,
            }),
        ).toBeDisabled();
        expect(
            screen.getByRole("checkbox", { name: /Infobereich/ }),
        ).toBeDisabled();
    });
    it("sends password with settings so reconnect uses the new credential", async () => {
        const user = userEvent.setup();
        renderSettings();
        await user.type(
            await screen.findByLabelText("WebSocket-Passwort"),
            "contract-password",
        );
        await user.click(screen.getByRole("button", { name: "Speichern" }));
        await waitFor(() =>
            expect(invokeMock).toHaveBeenCalledWith(
                "save_settings",
                expect.objectContaining({ obsPassword: "contract-password" }),
            ),
        );
        expect(
            invokeMock.mock.calls.some((c) => c[0] === "set_obs_password"),
        ).toBe(false);
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
