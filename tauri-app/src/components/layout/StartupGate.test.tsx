import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi } from "vitest";
import { StartupGate } from "./StartupGate";
import { tauriInvoke } from "../../lib/api";
vi.mock("../../lib/api", () => ({ tauriInvoke: vi.fn() }));
function show() {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <StartupGate>
                <p>App ready</p>
            </StartupGate>
        </QueryClientProvider>,
    );
}
describe("startup state", () => {
    it("shows fatal startup failure without loading the app", async () => {
        vi.mocked(tauriInvoke).mockResolvedValue(
            "settings.json cannot be read",
        );
        show();
        expect(await screen.findByRole("alert")).toHaveTextContent(
            "settings.json cannot be read",
        );
        expect(screen.queryByText("App ready")).toBeNull();
    });
    it("keeps settings accessible while reporting a failed overlay listener", async () => {
        vi.mocked(tauriInvoke).mockImplementation(async (...args) =>
            args[0] === "startup_error"
                ? null
                : { running: false, error: "Port 8765 belegt" },
        );
        show();
        expect(await screen.findByText("App ready")).toBeInTheDocument();
        expect(await screen.findByRole("alert")).toHaveTextContent(
            "Port 8765 belegt",
        );
    });
});
