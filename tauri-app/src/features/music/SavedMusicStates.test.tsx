import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { defaultAppSettings } from "../../lib/app-settings";
import { SavedMusicStates } from "./SavedMusicStates";
import type { MusicStateSnapshot } from "./music-state-types";
const invoke = vi.fn(),
    open = vi.fn(),
    save = vi.fn();
let changed: (() => void) | undefined;
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (command: string, args: unknown) => invoke(command, args),
    listenMusicStatesChanged: async (listener: () => void) => {
        changed = listener;
        return () => {
            changed = undefined;
        };
    },
}));
vi.mock("@tauri-apps/plugin-dialog", () => ({
    open: (args: unknown) => open(args),
    save: (args: unknown) => save(args),
}));
const entry = "10:00:00 · Intro: Song gespeichert";
let snapshot: MusicStateSnapshot;
let settings: ReturnType<typeof defaultAppSettings>;
beforeEach(() => {
    settings = defaultAppSettings();
    Object.assign(settings.Spotify, { Custom: { keep: 42 } });
    snapshot = {
        states: {
            Intro: {
                ContextUri: "spotify:playlist:list",
                Track: { Name: "Song", Artist: "Artist", DurationMs: 90000 },
                ProgressMs: 1234,
                VolumePercent: 64,
                ShuffleEnabled: true,
                RepeatMode: "context",
                WasPlaying: false,
                SavedAtUtc: new Date().toISOString(),
            },
        },
        history: {
            SavedCount: 1,
            RestoredCount: 0,
            DiscardedCount: 0,
            CleanupCount: 0,
            Entries: [entry],
            FavoriteEntries: [],
            Notes: {},
            SearchText: "",
            ActionFilterIndex: 0,
            SortIndex: 0,
            FavoritesOnly: false,
        },
        visibleHistory: [entry],
        profiles: [
            {
                Name: "Nur Verlauf zusammenführen",
                Entries: true,
                MergeEntries: true,
                Favorites: false,
                Notes: false,
                Counters: false,
                Filters: false,
                IsBuiltIn: true,
            },
        ],
        backups: [
            { id: "backup.json", at: new Date().toISOString(), bytes: 123 },
        ],
        health: {
            detail: "Bereit / pausiert",
            error: null,
            lastRecovery: null,
        },
    };
    invoke.mockReset();
    open.mockReset();
    save.mockReset();
    invoke.mockImplementation(async (command, args) => {
        if (command === "get_settings") return settings;
        if (command === "music_state_snapshot") return snapshot;
        if (args?.action?.action === "backup_preview")
            return {
                backup: snapshot.history,
                original: snapshot.history,
                added: ["new"],
                removed: [],
                unchanged: 1,
            };
        if (args?.action?.action === "profiles_preview")
            return {
                original: [],
                profiles: [
                    {
                        status: "changed",
                        profile: { Name: "Eigenes Profil", Entries: true },
                    },
                ],
            };
        return { success: true };
    });
});
function mount() {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return render(
        <QueryClientProvider client={client}>
            <SavedMusicStates />
        </QueryClientProvider>,
    );
}
it("captures groups, restores with fade and offers a separate stop during pending restore", async () => {
    mount();
    const user = userEvent.setup();
    await screen.findByText("Song · Artist");
    await user.clear(screen.getByLabelText("Zustandsgruppe"));
    await user.type(screen.getByLabelText("Zustandsgruppe"), "Pause");
    await user.click(
        screen.getByRole("button", { name: "Wiedergabe sichern" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_state_action", {
            action: { action: "capture", group: "Pause" },
        }),
    );
    await user.clear(screen.getByLabelText("Restore-Fade (Sekunden)"));
    await user.type(screen.getByLabelText("Restore-Fade (Sekunden)"), "3");
    let finish!: (value: unknown) => void;
    invoke.mockImplementation(async (command, args) =>
        command === "music_state_action" && args.action.action === "restore"
            ? new Promise((resolve) => {
                  finish = resolve;
              })
            : command === "get_settings"
              ? settings
              : command === "music_state_snapshot"
                ? snapshot
                : {},
    );
    await user.click(
        screen.getByRole("button", { name: "Intro wiederherstellen" }),
    );
    expect(invoke).toHaveBeenCalledWith("music_state_action", {
        action: { action: "restore", group: "Intro", fadeSeconds: 3 },
    });
    await user.click(
        screen.getByRole("button", { name: "Wiederherstellung abbrechen" }),
    );
    expect(invoke).toHaveBeenCalledWith("music_automation_action", {
        action: { action: "stop" },
    });
    finish({ success: true });
});
it("edits selected history notes and favorites, exports through native dialog and cancels without writes", async () => {
    mount();
    const user = userEvent.setup();
    await user.click(
        await screen.findByLabelText(`Verlauf auswählen: ${entry}`),
    );
    await user.type(screen.getByLabelText("Verlaufsnotiz"), "Merken");
    await user.click(screen.getByRole("button", { name: "Notiz speichern" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_state_action", {
            action: {
                action: "history_edit",
                entries: [entry],
                favorite: null,
                note: "Merken",
                remove: false,
            },
        }),
    );
    await user.click(screen.getByRole("button", { name: "Favorit setzen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_state_action", {
            action: {
                action: "history_edit",
                entries: [entry],
                favorite: true,
                note: null,
                remove: false,
            },
        }),
    );
    save.mockResolvedValueOnce("D:/history.csv");
    await user.click(
        screen.getByRole("button", { name: "Auswahl als CSV exportieren" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_state_action", {
            action: {
                action: "history_export",
                path: "D:/history.csv",
                entries: [entry],
                csv: true,
            },
        }),
    );
    open.mockResolvedValueOnce(null);
    await user.click(
        screen.getByRole("button", { name: "Verlauf importieren" }),
    );
    await waitFor(() => expect(open).toHaveBeenCalled());
    expect(
        invoke.mock.calls.filter(
            ([, args]) => args?.action?.action === "history_import",
        ),
    ).toHaveLength(0);
});
it("previews backup differences and passes the reviewed original and selective flags", async () => {
    mount();
    const user = userEvent.setup();
    await user.click(
        await screen.findByRole("button", {
            name: "Sicherung prüfen: backup.json",
        }),
    );
    expect(
        await screen.findByText("1 hinzugefügt · 0 entfernt · 1 unverändert"),
    ).toBeInTheDocument();
    await user.click(screen.getByLabelText("Notizen wiederherstellen"));
    await user.click(
        screen.getByRole("button", { name: "Geprüfte Sicherung anwenden" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_state_action", {
            action: {
                action: "backup_restore",
                id: "backup.json",
                original: snapshot.history,
                options: expect.objectContaining({
                    Entries: true,
                    MergeEntries: true,
                    Notes: true,
                }),
            },
        }),
    );
});
it("reviews profile import before writes and supports copying conflicts", async () => {
    mount();
    const user = userEvent.setup();
    open.mockResolvedValueOnce("D:/profiles.json");
    await screen.findByText("Song · Artist");
    await user.click(
        screen.getByRole("button", { name: "Profile importieren" }),
    );
    const review = await screen.findByRole("group", {
        name: "Profilimport prüfen",
    });
    expect(
        within(review).getByText("Eigenes Profil · geändert"),
    ).toBeInTheDocument();
    expect(
        invoke.mock.calls.filter(
            ([, args]) => args?.action?.action === "profiles_import",
        ),
    ).toHaveLength(0);
    await user.selectOptions(
        within(review).getByLabelText("Importaktion: Eigenes Profil"),
        "copy",
    );
    await user.click(
        within(review).getByRole("button", {
            name: "Geprüfte Profile importieren",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_state_action", {
            action: {
                action: "profiles_import",
                original: [],
                proposals: [
                    {
                        status: "changed",
                        profile: { Name: "Eigenes Profil", Entries: true },
                    },
                ],
                actions: ["copy"],
            },
        }),
    );
});
it("keeps failed settings drafts and refreshes snapshots from native events with cleanup", async () => {
    const view = mount();
    const user = userEvent.setup();
    await screen.findByText("Song · Artist");
    await user.clear(screen.getByLabelText("Zustandsalter (Minuten)"));
    await user.type(screen.getByLabelText("Zustandsalter (Minuten)"), "120");
    invoke.mockRejectedValueOnce(new Error("Speichern fehlgeschlagen"));
    await user.click(
        screen.getByRole("button", { name: "Zustandseinstellungen speichern" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
        "Speichern fehlgeschlagen",
    );
    expect(screen.getByLabelText("Zustandsalter (Minuten)")).toHaveValue(120);
    expect(invoke).toHaveBeenCalledWith("save_settings", {
        original: settings,
        settings: expect.objectContaining({
            Spotify: expect.objectContaining({
                SavedStateMaxAgeMinutes: 120,
                Custom: { keep: 42 },
            }),
        }),
    });
    const count = invoke.mock.calls.filter(
        ([cmd]) => cmd === "music_state_snapshot",
    ).length;
    changed?.();
    await waitFor(() =>
        expect(
            invoke.mock.calls.filter(([cmd]) => cmd === "music_state_snapshot")
                .length,
        ).toBeGreaterThan(count),
    );
    expect(screen.getByLabelText("Zustandsalter (Minuten)")).toHaveValue(120);
    view.unmount();
    expect(changed).toBeUndefined();
});

it("keeps a failed backup restore review and requires confirmation before deleting states", async () => {
    mount();
    const user = userEvent.setup();
    await user.click(
        await screen.findByRole("button", {
            name: "Sicherung prüfen: backup.json",
        }),
    );
    await screen.findByRole("group", { name: "Sicherungsvorschau" });
    invoke.mockRejectedValueOnce(
        new Error("Verlauf wurde inzwischen geändert"),
    );
    await user.click(
        screen.getByRole("button", { name: "Geprüfte Sicherung anwenden" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
        "inzwischen geändert",
    );
    expect(
        screen.getByRole("group", { name: "Sicherungsvorschau" }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Intro verwerfen" }));
    expect(
        invoke.mock.calls.filter(
            ([, args]) => args?.action?.action === "discard",
        ),
    ).toHaveLength(0);
    await user.click(
        screen.getByRole("button", { name: "Verwerfen bestätigen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("music_state_action", {
            action: { action: "discard", group: "Intro" },
        }),
    );
});

it("opens owned data and backup folders using the native commands", async () => {
    mount();
    const user = userEvent.setup();
    await screen.findByText("Song · Artist");
    await user.click(
        screen.getByRole("button", { name: "Sicherungsordner öffnen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("open_music_state_folder", {
            backups: true,
        }),
    );
    await user.click(
        screen.getByRole("button", { name: "Verlaufsordner öffnen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("open_music_state_folder", {
            backups: false,
        }),
    );
});
