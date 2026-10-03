import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
    act,
    fireEvent,
    render,
    screen,
    waitFor,
} from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import { TwitchChat } from "./TwitchChat";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (...args: unknown[]) => invoke(...args),
    listenTwitchEvents: async () => () => {},
    listenChatCatalog: async () => () => {},
}));
beforeEach(() => {
    invoke.mockReset();
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "twitch_chat_feed" ? { events: [] } : null,
    );
});
function setup() {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <TwitchChat enabled />
        </QueryClientProvider>,
    );
}
it("retains new text entered while an earlier message is being sent", async () => {
    let finish: () => void = () => {};
    invoke.mockImplementation((cmd: string) =>
        cmd === "twitch_action"
            ? new Promise<void>((resolve) => {
                  finish = resolve;
              })
            : Promise.resolve(
                  cmd === "twitch_chat_feed" ? { events: [] } : null,
              ),
    );
    setup();
    fireEvent.change(screen.getByLabelText("Chatnachricht"), {
        target: { value: "First" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Senden" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("twitch_action", {
            action: { action: "send_chat", message: "First" },
        }),
    );
    fireEvent.change(screen.getByLabelText("Chatnachricht"), {
        target: { value: "Next" },
    });
    await act(async () => finish());
    expect(screen.getByLabelText("Chatnachricht")).toHaveValue("Next");
});
it("keeps unsent text when Twitch rejects sending", async () => {
    invoke.mockImplementation(async (cmd: string) => {
        if (cmd === "twitch_action") throw Error("Token abgelaufen");
        return cmd === "twitch_chat_feed" ? { events: [] } : null;
    });
    setup();
    fireEvent.change(screen.getByLabelText("Chatnachricht"), {
        target: { value: "Retry" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Senden" }));
    expect(await screen.findByText(/Token abgelaufen/)).toBeInTheDocument();
    expect(screen.getByLabelText("Chatnachricht")).toHaveValue("Retry");
});
