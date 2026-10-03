import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    createMemoryHistory,
    createRootRoute,
    createRoute,
    createRouter,
    RouterProvider,
} from "@tanstack/react-router";
import {
    act,
    fireEvent,
    render,
    screen,
    waitFor,
} from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import {
    DashboardConfiguredScenes,
    DashboardAudioMixer,
    DashboardRaidAssistant,
    DashboardProfile,
} from "./DashboardShortcuts";
import { defaultAppSettings } from "../../lib/app-settings";
const invoke = vi.fn();
let raidChanged = () => {};
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    FALLBACK_POLL_MS: 15000,
    queryKeys: {
        settings: ["settings"],
        obsCurrentScene: ["obs-current-scene"],
    },
    listenTwitchRaids: async (fn: () => void) => {
        raidChanged = fn;
        return () => {};
    },
}));
beforeEach(() => {
    invoke.mockReset();
    raidChanged = () => {};
    vi.spyOn(window, "confirm").mockReturnValue(true);
});
function show(
    children: React.ReactNode,
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
    const root = createRootRoute();
    const route = createRoute({
        getParentRoute: () => root,
        path: "/",
        component: () => children,
    });
    const router = createRouter({
        routeTree: root.addChildren([route]),
        history: createMemoryHistory({ initialEntries: ["/"] }),
    });
    return {
        ...render(
            <QueryClientProvider client={client}>
                <RouterProvider router={router} />
            </QueryClientProvider>,
        ),
        client,
    };
}
it("keeps configured scene shortcuts separate from custom buttons and reports a rejected change", async () => {
    const settings = defaultAppSettings();
    settings.Obs.StartScene = "Intro";
    settings.Obs.LiveScene = "Game";
    settings.Obs.PauseScene = "Privacy";
    settings.Obs.EndScene = "";
    invoke
        .mockRejectedValueOnce(Error("Missing scene"))
        .mockResolvedValue(null);
    show(
        <DashboardConfiguredScenes
            settings={settings}
            enabled
            currentScene="Game"
        />,
    );
    expect(
        await screen.findByRole("button", { name: /Liveszene/ }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: /Endszene/ })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: /Startszene/ }));
    expect(await screen.findByText(/Missing scene/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Pauseszene/ }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_set_scene", {
            scene: "Privacy",
        }),
    );
    expect(invoke.mock.calls.every(([cmd]) => cmd !== "obs_control")).toBe(
        true,
    );
});
it("controls selected OBS audio, preserves an edited dB value during refresh and disables unsupported sources", async () => {
    let db = -12;
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "obs_query") {
            if (args.query.query === "inputs")
                return {
                    inputs: [{ inputName: "Mic" }, { inputName: "Camera" }],
                };
            if (args.query.inputName === "Camera")
                throw Error("No audio properties");
            if (args.query.query === "mute") return { inputMuted: false };
            return { inputVolumeDb: db };
        }
        return {};
    });
    const { client } = show(<DashboardAudioMixer enabled />);
    await screen.findByRole("option", { name: "Mic" });
    fireEvent.change(screen.getByLabelText("OBS-Audioquelle"), {
        target: { value: "Mic" },
    });
    const volume = await screen.findByLabelText("OBS-Lautstärke (dB)");
    await waitFor(() => expect(volume).toHaveValue(-12));
    fireEvent.change(volume, { target: { value: "-6" } });
    db = -20;
    await act(async () => {
        await client.invalidateQueries({ queryKey: ["obs-audio"] });
    });
    expect(volume).toHaveValue(-6);
    fireEvent.click(
        screen.getByRole("button", { name: "OBS-Lautstärke (dB) übernehmen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: {
                action: "set_volume",
                inputName: "Mic",
                inputVolumeDb: -6,
            },
        }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Stummschalten" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("obs_control", {
            control: { action: "set_mute", inputName: "Mic", inputMuted: true },
        }),
    );
    fireEvent.change(screen.getByLabelText("OBS-Audioquelle"), {
        target: { value: "Camera" },
    });
    expect(await screen.findByText(/No audio properties/)).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Stummschalten" }),
    ).toBeDisabled();
});
it("refreshes the persisted raid target, clears outdated success after a failure and follows selection events", async () => {
    let login = "target";
    let fail = false;
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "twitch_raid_settings")
            return { selected: login, channels: [login], original: {} };
        if (cmd === "list_profiles") return { profiles: [], warnings: [] };
        if (cmd === "get_settings") return defaultAppSettings();
        if (cmd === "twitch_raid_target") {
            if (fail) throw Error("Helix denied");
            return {
                login,
                displayName: login,
                isOnline: true,
                category: "Game",
                title: "Actual stream",
                viewerCount: 12,
                startedAt: "2026-10-03T10:00:00Z",
                profileImageUrl: "",
                channelUrl: `https://www.twitch.tv/${login}`,
            };
        }
        return null;
    });
    show(<DashboardRaidAssistant enabled />);
    expect(await screen.findByText("Actual stream")).toBeInTheDocument();
    fail = true;
    fireEvent.click(
        screen.getByRole("button", { name: "Raid-Ziel jetzt prüfen" }),
    );
    expect(await screen.findByText(/Helix denied/)).toBeInTheDocument();
    expect(screen.queryByText("Actual stream")).not.toBeInTheDocument();
    fail = false;
    login = "other";
    await act(async () => raidChanged());
    expect(await screen.findByText("other ist online")).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledWith("twitch_raid_target", {
        login: "other",
    });
});
it("applies a selected app profile with its actual settings snapshot, honors cancellation and exposes errors", async () => {
    const original = defaultAppSettings();
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "list_profiles")
            return {
                profiles: [{ id: "studio", name: "Studio" }],
                warnings: ["Broken profile retained"],
            };
        if (cmd === "get_settings") return original;
        if (cmd === "apply_profile") throw Error("Settings conflict");
        return null;
    });
    show(<DashboardProfile />);
    await screen.findByRole("option", { name: "Studio" });
    fireEvent.change(screen.getByLabelText("Schnellprofil"), {
        target: { value: "studio" },
    });
    expect(
        await screen.findByText("Broken profile retained"),
    ).toBeInTheDocument();
    vi.mocked(window.confirm).mockReturnValue(false);
    fireEvent.click(screen.getByRole("button", { name: "Profil anwenden" }));
    expect(invoke.mock.calls.some(([cmd]) => cmd === "apply_profile")).toBe(
        false,
    );
    vi.mocked(window.confirm).mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: "Profil anwenden" }));
    expect(await screen.findByText(/Settings conflict/)).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledWith("apply_profile", {
        id: "studio",
        original,
    });
    expect(
        invoke.mock.calls.every(
            ([cmd]) => !cmd.includes("workflow") && cmd !== "obs_control",
        ),
    ).toBe(true);
});

it("shows successful profile application and backend warnings after refreshing the saved settings", async () => {
    let settings = defaultAppSettings();
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "list_profiles")
            return {
                profiles: [{ id: "studio", name: "Studio" }],
                warnings: [],
            };
        if (cmd === "get_settings") return settings;
        if (cmd === "apply_profile") {
            settings = {
                ...settings,
                Obs: { ...settings.Obs, StartScene: "New intro" },
            };
            return { saved: true, warnings: ["OBS reconnect pending"] };
        }
        return null;
    });
    const { client } = show(<DashboardProfile />);
    await screen.findByRole("option", { name: "Studio" });
    fireEvent.change(screen.getByLabelText("Schnellprofil"), {
        target: { value: "studio" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Profil anwenden" }));
    expect(await screen.findByText("Profil angewendet.")).toBeInTheDocument();
    expect(screen.getByText("OBS reconnect pending")).toBeInTheDocument();
    expect(
        client.getQueryData<{ Obs: { StartScene: string } }>(["settings"])?.Obs
            .StartScene,
    ).toBe("New intro");
});

it("keeps controls disabled for incomplete audio replies instead of treating missing mute or volume as a known state", async () => {
    invoke.mockImplementation(async (cmd, args) =>
        cmd === "obs_query" && args.query.query === "inputs"
            ? { inputs: [{ inputName: "Unknown" }] }
            : {},
    );
    show(<DashboardAudioMixer enabled />);
    await screen.findByRole("option", { name: "Unknown" });
    fireEvent.change(screen.getByLabelText("OBS-Audioquelle"), {
        target: { value: "Unknown" },
    });
    expect(
        await screen.findByText(
            "Keine steuerbaren Audioeigenschaften verfügbar.",
        ),
    ).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Stummschalten" }),
    ).toBeDisabled();
    expect(screen.getByLabelText("OBS-Lautstärke (dB)")).toBeDisabled();
});
