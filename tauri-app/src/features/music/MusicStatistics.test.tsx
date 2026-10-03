import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { MusicStatistics, type MusicStatisticsSnapshot } from "./MusicStatistics";
const invoke = vi.fn(),
    unlisten = vi.fn();
let changed: (() => void) | undefined;
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (command: string) => invoke(command),
    listenMusicStatisticsChanged: async (listener: () => void) => {
        changed = listener;
        return unlisten;
    },
}));
const empty = {
    totalPlays: 0,
    totalListeningSeconds: 0,
    topTracks: [],
    topArtists: [],
    error: null,
};
const populated = {
    totalPlays: 7,
    totalListeningSeconds: 90061,
    topTracks: [
        {
            TrackId: "song",
            Title: "Song",
            Artist: "Artist",
            Album: "Album",
            PlayCount: 7,
            ListeningSeconds: 90061,
            LastPlayedAt: "2026-01-01T12:00:00Z",
        },
    ],
    topArtists: [{ artist: "Artist", playCount: 7, listeningSeconds: 90061 }],
    error: null,
};
let snapshot: MusicStatisticsSnapshot;
beforeEach(() => {
    invoke.mockReset();
    unlisten.mockReset();
    changed = undefined;
    snapshot = structuredClone(populated);
    invoke.mockImplementation(async (command) => {
        if (command === "music_statistics_snapshot") return snapshot;
        if (command === "reset_music_statistics") {
            snapshot = empty;
            return null;
        }
        throw new Error(command);
    });
});
function mount() {
    const client = new QueryClient({
        defaultOptions: { queries: { retry: false } },
    });
    return render(
        <QueryClientProvider client={client}>
            <MusicStatistics />
        </QueryClientProvider>,
    );
}
it("shows persisted totals and top tracks/artists with durations longer than one day", async () => {
    mount();
    await screen.findByText("7 erkannte Titel");
    expect(screen.getAllByText("25:01:01")).toHaveLength(3);
    const tracks = screen.getByRole("table", { name: "Häufigste Titel" });
    expect(within(tracks).getByText("Song")).toBeInTheDocument();
    expect(within(tracks).getByText("Album")).toBeInTheDocument();
    expect(
        within(
            screen.getByRole("table", { name: "Häufigste Interpreten" }),
        ).getByText("Artist"),
    ).toBeInTheDocument();
});
it("requires reset confirmation, allows cancellation and refreshes empty persisted state", async () => {
    const user = userEvent.setup();
    mount();
    await screen.findByText("7 erkannte Titel");
    await user.click(
        screen.getByRole("button", { name: "Statistik zurücksetzen" }),
    );
    expect(invoke).not.toHaveBeenCalledWith("reset_music_statistics");
    await user.click(screen.getByRole("button", { name: "Abbrechen" }));
    expect(
        screen.queryByRole("button", { name: "Endgültig zurücksetzen" }),
    ).not.toBeInTheDocument();
    await user.click(
        screen.getByRole("button", { name: "Statistik zurücksetzen" }),
    );
    await user.click(
        screen.getByRole("button", { name: "Endgültig zurücksetzen" }),
    );
    await screen.findByText("Noch keine Hörstatistik vorhanden.");
    expect(invoke).toHaveBeenCalledWith("reset_music_statistics");
    expect(screen.getByText("0 erkannte Titel")).toBeInTheDocument();
});
it("retains statistics and confirmation when reset fails and surfaces background errors", async () => {
    const user = userEvent.setup();
    snapshot = { ...populated, error: "Spotify offline" };
    invoke.mockImplementation(async (command) => {
        if (command === "music_statistics_snapshot") return snapshot;
        throw new Error("Speicher schreibgeschützt");
    });
    mount();
    await screen.findByText("Spotify offline");
    await user.click(
        screen.getByRole("button", { name: "Statistik zurücksetzen" }),
    );
    await user.click(
        screen.getByRole("button", { name: "Endgültig zurücksetzen" }),
    );
    await screen.findByText(/Speicher schreibgeschützt/);
    expect(screen.getByText("7 erkannte Titel")).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Endgültig zurücksetzen" }),
    ).toBeEnabled();
});
it("refreshes native changes, unsubscribes and allows retry after loading failure", async () => {
    invoke.mockRejectedValueOnce(new Error("Defekte Statistikdatei"));
    const view = mount();
    await screen.findByText(/Defekte Statistikdatei/);
    await userEvent
        .setup()
        .click(screen.getByRole("button", { name: "Aktualisieren" }));
    await screen.findByText("7 erkannte Titel");
    await waitFor(() => expect(changed).toBeTypeOf("function"));
    snapshot = empty;
    act(() => changed!());
    await screen.findByText("Noch keine Hörstatistik vorhanden.");
    view.unmount();
    expect(unlisten).toHaveBeenCalledOnce();
});
