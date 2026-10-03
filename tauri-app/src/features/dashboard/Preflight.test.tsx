import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { Preflight } from "./Preflight";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (command: string) => invoke(command),
}));
it("runs on demand, shows individual checks and never starts a stream; retry failures remain visible", async () => {
    invoke.mockResolvedValue({
        checkedAt: "2026-10-03T12:00:00Z",
        warningCount: 1,
        checks: [
            { key: "obs", label: "OBS verbunden", ok: true, detail: "" },
            {
                key: "title",
                label: "Streamtitel gesetzt",
                ok: false,
                detail: "Channel denied",
            },
        ],
    });
    render(
        <QueryClientProvider client={new QueryClient()}>
            <Preflight />
        </QueryClientProvider>,
    );
    expect(invoke).not.toHaveBeenCalled();
    fireEvent.click(
        screen.getByRole("button", { name: "Vorprüfung ausführen" }),
    );
    expect(await screen.findByText("Channel denied")).toBeInTheDocument();
    expect(
        screen.getByText("1 Punkt benötigt Aufmerksamkeit."),
    ).toBeInTheDocument();
    expect(invoke.mock.calls.map(([command]) => command)).toEqual([
        "dashboard_preflight",
    ]);
    invoke.mockRejectedValue(Error("Settings beschädigt"));
    fireEvent.click(
        screen.getByRole("button", { name: "Vorprüfung ausführen" }),
    );
    await waitFor(() =>
        expect(screen.getByRole("alert")).toHaveTextContent(
            "Settings beschädigt",
        ),
    );
    expect(screen.getByText(/Vorherige Prüfung/)).toBeInTheDocument();
});
