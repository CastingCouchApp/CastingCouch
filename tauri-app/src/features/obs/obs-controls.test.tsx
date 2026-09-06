import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { ObsControls } from "./ObsControls";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
function show() {
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <ObsControls enabled />
        </QueryClientProvider>,
    );
}
describe("OBS output availability", () => {
    it("keeps stream controls available when the virtual camera is unavailable", async () => {
        invoke.mockResolvedValue({
            stream: { outputActive: true },
            record: { outputActive: false },
            replay: { outputActive: false },
            camera: null,
            stats: { activeFps: 60 },
            errors: { camera: "Camera unavailable" },
        });
        show();
        expect(await screen.findByText("Stream läuft")).toBeInTheDocument();
        expect(
            screen.getByRole("button", { name: "Stream stoppen" }),
        ).toBeEnabled();
        expect(
            screen.getByRole("button", { name: "Virtuelle Kamera starten" }),
        ).toBeDisabled();
        expect(screen.getByText(/Camera unavailable/)).toBeInTheDocument();
    });
    it("does not present a failed status request as a stopped stream", async () => {
        invoke.mockRejectedValue(new Error("Disconnected"));
        show();
        expect(await screen.findByRole("alert")).toHaveTextContent(
            "Disconnected",
        );
        expect(screen.queryByText("Stream gestoppt")).not.toBeInTheDocument();
        expect(
            screen.getByRole("button", { name: "Stream starten" }),
        ).toBeDisabled();
    });
});
