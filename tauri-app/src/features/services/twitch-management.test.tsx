import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { TwitchRewards } from "./TwitchRewards";
import { TwitchVotes } from "./TwitchVotes";

const invoke = vi.fn();
const listeners: ((event: Record<string, unknown>) => void)[] = [];
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenTwitchEvents: async (
        listener: (event: Record<string, unknown>) => void,
    ) => {
        listeners.push(listener);
        return () => {
            listeners.splice(listeners.indexOf(listener), 1);
        };
    },
}));
function show(node: React.ReactNode) {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            {node}
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    listeners.length = 0;
});

it("edits reward fields, shows redemption input, pages, and exposes errors", async () => {
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "twitch_action")
            throw new Error("Reward belongs to another app");
        if (args.query.query === "rewards")
            return {
                data: [
                    {
                        id: "r",
                        title: "Song",
                        cost: 150,
                        prompt: "Request",
                        is_enabled: true,
                        is_paused: false,
                        is_user_input_required: true,
                        background_color: "#123456",
                    },
                ],
            };
        return {
            data: [
                {
                    id: "x",
                    user_name: "Alice",
                    user_input: "My song",
                    status: "UNFULFILLED",
                },
            ],
            pagination: { cursor: "next" },
        };
    });
    const user = userEvent.setup();
    show(<TwitchRewards enabled />);
    await user.click(await screen.findByRole("button", { name: "Bearbeiten" }));
    const title = screen.getByLabelText("Reward-Titel");
    expect(title).toHaveValue("Song");
    await user.clear(title);
    await user.type(title, "Neue Musik");
    await user.click(screen.getByRole("button", { name: "Reward speichern" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_action", {
            action: expect.objectContaining({
                action: "update_reward",
                id: "r",
                title: "Neue Musik",
                prompt: "Request",
                cost: 150,
                isUserInputRequired: true,
            }),
        }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent("another app");
    await user.click(screen.getByRole("button", { name: "Einlösungen" }));
    expect(await screen.findByText("My song")).toBeInTheDocument();
    await user.click(
        screen.getByRole("button", { name: "Nächste Einlösungen" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_query", {
            query: {
                query: "redemptions",
                rewardId: "r",
                status: "UNFULFILLED",
            },
            after: "next",
        }),
    );
});

it("keeps a reward when deletion is canceled and refreshes on reward events", async () => {
    invoke.mockResolvedValue({ data: [{ id: "r", title: "Song", cost: 100 }] });
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const user = userEvent.setup();
    const view = show(<TwitchRewards enabled />);
    await user.click(
        await screen.findByRole("button", { name: "Reward löschen" }),
    );
    expect(invoke.mock.calls.some(([cmd]) => cmd === "twitch_action")).toBe(
        false,
    );
    const count = invoke.mock.calls.length;
    listeners.forEach((fn) =>
        fn({ type: "channel.channel_points_custom_reward.update" }),
    );
    await waitFor(() =>
        expect(invoke.mock.calls.length).toBeGreaterThan(count),
    );
    view.unmount();
    expect(listeners).toHaveLength(0);
    confirm.mockRestore();
});

it("locks predictions, shows results and refreshes after external changes", async () => {
    invoke.mockImplementation(async (cmd, args) => {
        if (cmd === "twitch_action") return { data: [] };
        if (args.query.query === "polls")
            return {
                data: [
                    {
                        id: "poll",
                        title: "Game?",
                        status: "ACTIVE",
                        choices: [{ id: "a", title: "Chess", votes: 12 }],
                    },
                ],
            };
        return {
            data: [
                {
                    id: "p",
                    title: "Win?",
                    status: "ACTIVE",
                    outcomes: [
                        {
                            id: "a",
                            title: "Yes",
                            users: 3,
                            channel_points: 400,
                        },
                    ],
                },
            ],
        };
    });
    const user = userEvent.setup();
    show(<TwitchVotes enabled />);
    expect(await screen.findByText(/Chess.*12/)).toBeInTheDocument();
    await user.click(
        await screen.findByRole("button", { name: "Vorhersage sperren" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_action", {
            action: {
                action: "end_prediction",
                id: "p",
                status: "LOCKED",
                winningOutcomeId: null,
            },
        }),
    );
    const count = invoke.mock.calls.filter(
        ([, args]) => args?.query?.query === "polls",
    ).length;
    listeners.forEach((fn) => fn({ type: "channel.poll.progress" }));
    await waitFor(() =>
        expect(
            invoke.mock.calls.filter(
                ([, args]) => args?.query?.query === "polls",
            ).length,
        ).toBeGreaterThan(count),
    );
    expect(screen.getByText(/Yes.*400/)).toBeInTheDocument();
});

it("uses separate poll and prediction creation fields and rejects invalid drafts", async () => {
    invoke.mockResolvedValue({ data: [] });
    const user = userEvent.setup();
    show(<TwitchVotes enabled />);
    const poll = screen
        .getByRole("heading", { name: "Umfragen" })
        .closest("section")!;
    await user.type(within(poll).getByLabelText("Umfrage-Titel"), "Game?");
    await user.clear(within(poll).getByLabelText("Umfrage-Antworten"));
    await user.type(within(poll).getByLabelText("Umfrage-Antworten"), "Single");
    expect(
        within(poll).getByRole("button", { name: "Umfrage starten" }),
    ).toBeDisabled();
    await user.type(
        within(poll).getByLabelText("Umfrage-Antworten"),
        "\nAnother",
    );
    await user.click(
        within(poll).getByRole("button", { name: "Umfrage starten" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_action", {
            action: {
                action: "create_poll",
                title: "Game?",
                choices: ["Single", "Another"],
                duration: 60,
            },
        }),
    );
});
