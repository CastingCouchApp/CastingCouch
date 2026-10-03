import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { ChatCatalogStatusPanel } from "./ChatCatalogStatus";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({ tauriInvoke: (cmd: string) => invoke(cmd) }));
beforeEach(() => {
    invoke.mockReset();
});
function show(enabled = true) {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <ChatCatalogStatusPanel enabled={enabled} />
        </QueryClientProvider>,
    );
}
it("shows catalog counts and provider errors, then refreshes through the native command", async () => {
    let status = {
        emotes: 7,
        badges: 2,
        errors: ["7TV: HTTP 503"],
        updatedAt: "2026-10-03",
    };
    invoke.mockImplementation(async (cmd: string) => {
        if (cmd === "refresh_chat_catalogs")
            status = { ...status, emotes: 12, errors: [] };
        return status;
    });
    show();
    expect(await screen.findByText("7 Emotes · 2 Badges")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent("7TV: HTTP 503");
    await userEvent.click(
        screen.getByRole("button", { name: "Chat-Kataloge aktualisieren" }),
    );
    await screen.findByText("12 Emotes · 2 Badges");
    expect(invoke).toHaveBeenCalledWith("refresh_chat_catalogs");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});
it("keeps loading errors visible and prevents refresh without a Twitch connection", async () => {
    invoke.mockRejectedValue(new Error("Settings unreadable"));
    show(false);
    await waitFor(() =>
        expect(screen.getByRole("alert")).toHaveTextContent(
            "Settings unreadable",
        ),
    );
    expect(
        screen.getByRole("button", { name: "Chat-Kataloge aktualisieren" }),
    ).toBeDisabled();
    expect(screen.queryByText("0 Emotes · 0 Badges")).not.toBeInTheDocument();
});
