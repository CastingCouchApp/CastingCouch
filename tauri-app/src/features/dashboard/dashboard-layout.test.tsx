import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    act,
    fireEvent,
    render,
    screen,
    waitFor,
} from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { DashboardLayout, DashboardCards } from "./DashboardLayout";
import { DashboardSceneButtons } from "./DashboardSceneButtons";
import type { DashboardSnapshot } from "./dashboard-types";

const invoke = vi.fn();
let changed = () => {};
vi.mock("../../lib/api", () => ({
    tauriInvoke: (...args: unknown[]) => invoke(...args),
    FALLBACK_POLL_MS: 15000,
    queryKeys: {
        obsScenes: ["obs-scenes"],
        obsCurrentScene: ["obs-current-scene"],
        settings: ["settings"],
    },
    listenDashboardChanged: async (fn: () => void) => {
        changed = fn;
        return () => {};
    },
}));
function snapshot(): DashboardSnapshot {
    return {
        original: { Dashboard: { Future: 7 } },
        sceneChoices: ["Live", "Pause"],
        warnings: [],
        draft: {
            cards: [
                {
                    key: "ConnectionStatus",
                    visible: true,
                    zone: "Left",
                    size: "Standard",
                },
                {
                    key: "StreamControl",
                    visible: true,
                    zone: "Center",
                    size: "Standard",
                },
                {
                    key: "StreamHistory",
                    visible: true,
                    zone: "Center",
                    size: "Standard",
                },
            ],
            sceneButtons: [
                {
                    id: "stable",
                    title: "Gaming",
                    sceneName: "Live",
                    iconKind: "Emoji",
                    iconValue: "🎮",
                    color: "#ff0011",
                },
            ],
            preferences: {
                showServiceStatus: true,
                showStreamControls: true,
                showLivePanels: true,
                showQuickServices: true,
                showAdvancedTools: true,
                showNotifications: true,
                showStreamHistory: true,
                autoFocusModeOnStreamStart: true,
                autoExitFocusModeOnStreamEnd: true,
                obsScenePreviewSize: "Standard",
                dashboardStatistic: "ViewerCount",
                streamEndExpanded: false,
            },
        },
    };
}
let latest: DashboardSnapshot;
beforeEach(() => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    latest = snapshot();
    invoke.mockReset();
    invoke.mockImplementation(
        async (cmd: string, args?: { draft?: unknown }) => {
            if (cmd === "dashboard_snapshot") return structuredClone(latest);
            if (cmd === "save_dashboard")
                return { ...latest, draft: structuredClone(args?.draft) };
            if (cmd === "obs_scenes")
                return [{ name: "Live" }, { name: "Pause" }];
            if (cmd === "dashboard_asset_choices")
                return [{ id: "image", name: "Logo", path: "C:/image.png" }];
            if (cmd === "dashboard_image_preview")
                return "data:image/png;base64,eA==";
        },
    );
});
function setup(live?: boolean) {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    const view = (value?: boolean) => (
        <QueryClientProvider client={client}>
            <DashboardLayout live={value}>
                {(draft, focus) => (
                    <DashboardCards
                        draft={draft}
                        focus={focus}
                        nodes={{
                            ConnectionStatus: <p>Connections</p>,
                            StreamControl: <p>Controls</p>,
                            StreamHistory: <p>History</p>,
                        }}
                    />
                )}
            </DashboardLayout>
        </QueryClientProvider>
    );
    return { ...render(view(live)), live: (value?: boolean) => view(value) };
}
it("persists visibility, position, size and stable scene button metadata", async () => {
    setup();
    await screen.findByText("Connections");
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard konfigurieren" }),
    );
    fireEvent.click(screen.getByLabelText("Verbindungen anzeigen"));
    fireEvent.change(screen.getByLabelText("OBS-Ausgänge Größe"), {
        target: { value: "Groß" },
    });
    fireEvent.change(screen.getByLabelText("OBS-Ausgänge Spalte"), {
        target: { value: "Right" },
    });
    fireEvent.change(screen.getByLabelText("Buttonname Gaming"), {
        target: { value: "Interview" },
    });
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard speichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "save_dashboard",
            expect.objectContaining({
                original: latest.original,
                draft: expect.objectContaining({
                    sceneButtons: [
                        expect.objectContaining({
                            id: "stable",
                            title: "Interview",
                            color: "#ff0011",
                        }),
                    ],
                }),
            }),
        ),
    );
    await waitFor(() =>
        expect(screen.queryByText("Connections")).not.toBeInTheDocument(),
    );
    const saved = invoke.mock.calls.find((c) => c[0] === "save_dashboard")![1];
    expect(saved.draft.cards[1]).toMatchObject({ size: "Groß", zone: "Right" });
});
it("keeps a dirty draft across incoming settings events and preserves it on save failure", async () => {
    setup();
    await screen.findByText("Connections");
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard konfigurieren" }),
    );
    fireEvent.change(screen.getByLabelText("Buttonname Gaming"), {
        target: { value: "Unsaved" },
    });
    latest = snapshot();
    latest.draft.sceneButtons[0].title = "External";
    await act(async () => changed());
    await waitFor(() =>
        expect(
            invoke.mock.calls.filter((c) => c[0] === "dashboard_snapshot")
                .length,
        ).toBeGreaterThan(1),
    );
    expect(screen.getByLabelText("Buttonname Unsaved")).toHaveValue("Unsaved");
    invoke.mockImplementation(async (cmd: string) => {
        if (cmd === "save_dashboard") throw Error("Konflikt");
        return latest;
    });
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard speichern" }),
    );
    expect(await screen.findByText("Konflikt")).toBeInTheDocument();
    expect(screen.getByLabelText("Buttonname Unsaved")).toHaveValue("Unsaved");
});
it("focuses on verified stream start and restores cards after stream end, ignoring unknown status", async () => {
    const view = setup(false);
    await screen.findByText("History");
    view.rerender(view.live(true));
    await waitFor(() =>
        expect(screen.queryByText("History")).not.toBeInTheDocument(),
    );
    view.rerender(view.live(undefined));
    expect(screen.queryByText("History")).not.toBeInTheDocument();
    view.rerender(view.live(false));
    expect(await screen.findByText("History")).toBeInTheDocument();
    expect(invoke.mock.calls.some((c) => c[0] === "save_dashboard")).toBe(
        false,
    );
});
it("reorders cards and retains an intentionally empty button list", async () => {
    setup();
    await screen.findByText("Connections");
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard konfigurieren" }),
    );
    fireEvent.click(
        screen.getByRole("button", { name: "OBS-Ausgänge nach oben" }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Gaming entfernen" }));
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard speichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "save_dashboard",
            expect.objectContaining({
                draft: expect.objectContaining({ sceneButtons: [] }),
            }),
        ),
    );
    const saved = invoke.mock.calls.find((c) => c[0] === "save_dashboard")![1];
    expect(saved.draft.cards[0].key).toBe("StreamControl");
    await waitFor(() =>
        expect(
            document.querySelector<HTMLElement>(
                '[data-dashboard-card="StreamControl"]',
            )?.style.order,
        ).toBe("0"),
    );
});
it("applies the minimal preset while keeping saved scene definitions", async () => {
    setup();
    await screen.findByText("Connections");
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard konfigurieren" }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Minimal" }));
    fireEvent.click(
        screen.getByRole("button", { name: "Dashboard speichern" }),
    );
    await waitFor(() =>
        expect(screen.queryByText("History")).not.toBeInTheDocument(),
    );
    expect(screen.getByText("Connections")).toBeInTheDocument();
    const saved = invoke.mock.calls.find((c) => c[0] === "save_dashboard")![1];
    expect(saved.draft.sceneButtons).toEqual(latest.draft.sceneButtons);
    expect(saved.draft.preferences.showStreamHistory).toBe(false);
});
it("switches the exact OBS scene, highlights it and reports errors", async () => {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    invoke.mockImplementation(async (cmd: string) => {
        if (cmd === "obs_set_scene") throw Error("OBS getrennt");
    });
    render(
        <QueryClientProvider client={client}>
            <DashboardSceneButtons
                buttons={latest.draft.sceneButtons}
                enabled
                currentScene="LIVE"
            />
        </QueryClientProvider>,
    );
    const button = screen.getByRole("button", { name: /Gaming/ });
    expect(button).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(button);
    expect(await screen.findByText("OBS getrennt")).toBeInTheDocument();
    expect(invoke).toHaveBeenCalledWith("obs_set_scene", { scene: "Live" });
});
it("reports configuration read errors instead of enabling writes to a fallback layout", async () => {
    invoke.mockRejectedValue(Error("Einstellungen beschädigt"));
    setup();
    expect(
        await screen.findByText("Einstellungen beschädigt"),
    ).toBeInTheDocument();
    expect(
        screen.queryByRole("button", { name: "Dashboard konfigurieren" }),
    ).not.toBeInTheDocument();
});
