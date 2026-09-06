import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { it, expect, vi } from "vitest";
import { ObsSourceSetup } from "./ObsSourceSetup";
const invoke = vi.fn();
vi.mock("../../lib/api", () => ({
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
it("creates the selected canvas source only after submitting the setup form", async () => {
    invoke.mockImplementation(async (cmd: string) =>
        cmd === "obs_scenes"
            ? [{ name: "Live", index: 0 }]
            : { created: false },
    );
    render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <ObsSourceSetup
                canvases={[
                    {
                        id: "default",
                        name: "Canvas",
                        view_url: "http://127.0.0.1:8765/view/default",
                        editor_url: "http://127.0.0.1:8765/editor/default",
                    },
                ]}
            />
        </QueryClientProvider>,
    );
    await screen.findByRole("option", { name: "Live" });
    expect(invoke).not.toHaveBeenCalledWith(
        "setup_overlay_source",
        expect.anything(),
    );
    fireEvent.change(screen.getByLabelText("OBS-Zielszene"), {
        target: { value: "Live" },
    });
    fireEvent.change(screen.getByLabelText("OBS-Quellenname"), {
        target: { value: "My Canvas" },
    });
    fireEvent.click(
        screen.getByRole("button", {
            name: "Browserquelle anlegen / aktualisieren",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("setup_overlay_source", {
            canvasId: "default",
            sceneName: "Live",
            inputName: "My Canvas",
        }),
    );
    expect(await screen.findByRole("status")).toHaveTextContent(
        "Browserquelle aktualisiert",
    );
});
