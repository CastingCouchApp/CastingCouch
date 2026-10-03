import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { defaultAppSettings } from "../../lib/app-settings";
import { YouTubeMusicSetup } from "./YouTubeMusicSetup";
const invoke = vi.fn(),
    write = vi.fn();
let settings: ReturnType<typeof defaultAppSettings>;
let runtime: Record<string, unknown>;
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (command: string, args: unknown) => invoke(command, args),
}));
beforeEach(() => {
    invoke.mockReset();
    write.mockReset();
    Object.defineProperty(navigator, "clipboard", {
        value: { writeText: write },
        configurable: true,
    });
    write.mockResolvedValue(undefined);
    settings = defaultAppSettings();
    settings.YouTubeMusic = {
        BridgePort: 43831,
        StateTimeoutSeconds: 12,
        AutoConnect: true,
        Future: { keep: 42 },
    };
    runtime = {
        running: true,
        port: 43831,
        configuredPort: 43831,
        error: null,
        installUrl: "http://127.0.0.1:43831/ytmusic/install",
        bookmarklet: "javascript:%28example%29",
        snapshot: {
            title: "YT Song",
            artist: "Artist",
            album: "Album",
            coverUrl: "https://example.com/cover.png",
            connected: true,
            isPlaying: true,
            statusText: "Spielt",
            progressMs: 1000,
            durationMs: 60000,
        },
    };
    invoke.mockImplementation(async (command, args) => {
        if (command === "get_settings") return settings;
        if (command === "ytm_runtime_status") return runtime;
        if (command === "save_settings") {
            settings = args.settings;
            return { saved: true, warnings: [] };
        }
        if (command === "ytm_disconnect") {
            runtime = {
                ...runtime,
                running: false,
                snapshot: { statusText: "Bridge gestoppt", connected: false },
            };
            return null;
        }
        if (command === "ytm_command") return null;
        if (command === "ytm_connect") return runtime.installUrl;
        throw new Error(command);
    });
});
function mount() {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return render(
        <QueryClientProvider client={client}>
            <YouTubeMusicSetup />
        </QueryClientProvider>,
    );
}
it("shows actual bridge state and metadata, copies the inline bookmarklet and sends all supported commands", async () => {
    mount();
    const user = setupUser();
    await screen.findByText("YT Song");
    expect(
        screen.getByRole("img", { name: "Cover von YT Song" }),
    ).toHaveAttribute("src", "https://example.com/cover.png");
    expect(
        screen.getByRole("link", { name: "Install-Seite öffnen" }),
    ).toHaveAttribute("href", runtime.installUrl);
    await user.click(
        screen.getByRole("button", { name: "Bookmarklet kopieren" }),
    );
    expect(write).toHaveBeenCalledWith(runtime.bookmarklet);
    for (const [name, command] of [
        ["Vorheriger YouTube-Titel", "previous"],
        ["YouTube Music pausieren", "pause"],
        ["Nächster YouTube-Titel", "next"],
    ]) {
        await user.click(screen.getByRole("button", { name }));
        await waitFor(() =>
            expect(invoke).toHaveBeenCalledWith("ytm_command", { command }),
        );
    }
    await user.click(
        screen.getByRole("button", { name: "YouTube Music trennen" }),
    );
    await screen.findByText("Bridge gestoppt");
    expect(
        screen.getByRole("button", { name: "Nächster YouTube-Titel" }),
    ).toBeDisabled();
});
it("saves validated bridge preferences without dropping unknown fields and updates setup URL", async () => {
    mount();
    const user = setupUser();
    await screen.findByLabelText("YouTube-Music-Port");
    const port = screen.getByLabelText("YouTube-Music-Port");
    await user.clear(port);
    await user.type(port, "43900");
    const timeout = screen.getByLabelText("Bookmarklet-Timeout (Sekunden)");
    await user.clear(timeout);
    await user.type(timeout, "30");
    await user.click(
        screen.getByLabelText("YouTube Music beim Appstart verbinden"),
    );
    runtime = {
        ...runtime,
        port: 43900,
        configuredPort: 43900,
        installUrl: "http://127.0.0.1:43900/ytmusic/install",
    };
    await user.click(
        screen.getByRole("button", { name: "Bridge-Einstellungen speichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith(
            "save_settings",
            expect.objectContaining({
                original: expect.any(Object),
                settings: expect.objectContaining({
                    YouTubeMusic: expect.objectContaining({
                        BridgePort: 43900,
                        StateTimeoutSeconds: 30,
                        AutoConnect: false,
                        Future: { keep: 42 },
                    }),
                }),
            }),
        ),
    );
    await waitFor(() =>
        expect(
            screen.getByRole("link", { name: "Install-Seite öffnen" }),
        ).toHaveAttribute("href", runtime.installUrl),
    );
});
it("retains a failed settings draft, rejects invalid ports and provides manual copy after clipboard failure", async () => {
    mount();
    const user = setupUser();
    await screen.findByLabelText("YouTube-Music-Port");
    let port = screen.getByLabelText("YouTube-Music-Port");
    await user.clear(port);
    await user.type(port, "0");
    expect(
        screen.getByRole("button", { name: "Bridge-Einstellungen speichern" }),
    ).toBeDisabled();
    await user.clear(port);
    await user.type(port, "43900");
    invoke.mockImplementation(async (command) => {
        if (command === "get_settings") return settings;
        if (command === "ytm_runtime_status") return runtime;
        throw new Error("Port belegt");
    });
    await user.click(
        screen.getByRole("button", { name: "Bridge-Einstellungen speichern" }),
    );
    await screen.findByText(/Port belegt/);
    expect(port).toHaveValue(43900);
    write.mockRejectedValueOnce(new Error("Zwischenablage gesperrt"));
    await user.click(
        screen.getByRole("button", { name: "Bookmarklet kopieren" }),
    );
    await screen.findByText(/Zwischenablage gesperrt/);
    expect(
        screen.getByLabelText("Bookmarklet zum manuellen Kopieren"),
    ).toHaveValue(String(runtime.bookmarklet));
});

function setupUser() {
    const user = userEvent.setup();
    Object.defineProperty(navigator, "clipboard", {
        value: { writeText: write },
        configurable: true,
    });
    return user;
}
