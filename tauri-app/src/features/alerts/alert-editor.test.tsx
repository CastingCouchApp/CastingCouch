import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi } from "vitest";
import { AlertEditor } from "./AlertEditor";
import { defaultAppSettings } from "../../lib/app-settings";
import type { AlertDefinition } from "../../lib/api";
const invoke = vi.hoisted(() => vi.fn());
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: invoke,
}));
const settings = defaultAppSettings().Alerts.Definitions.Follow;
const initial: AlertDefinition = {
    type: "Follow",
    enabled: true,
    text_template: settings.TextTemplate,
    media_path: "",
    sound_path: "",
    duration_seconds: 8,
    priority: 100,
    font_face: "Inter",
    font_size: 44,
    font_color: "#FFFFFF",
    animation: "Fade",
    x: 0,
    y: 0,
    width: 900,
    height: 260,
    volume_percent: 70,
    sound_start_seconds: 0,
    sound_end_seconds: 0,
    audio_output_device_id: "default",
};
describe("alert designer", () => {
    it("edits name, sound range and animation and sends the complete definition", async () => {
        const user = userEvent.setup();
        const save = vi.fn();
        render(
            <QueryClientProvider client={new QueryClient()}>
                <AlertEditor
                    initial={initial}
                    onSave={save}
                    onClose={() => {}}
                    pending={false}
                />
            </QueryClientProvider>,
        );
        await user.clear(screen.getByLabelText("Name / Typ"));
        await user.type(screen.getByLabelText("Name / Typ"), "Special");
        await user.type(
            screen.getByLabelText("Soundpfad (lokale Datei)"),
            "C:/sounds/alert.wav",
        );
        await user.clear(screen.getByLabelText("Sound-Start (Sekunden)"));
        await user.type(screen.getByLabelText("Sound-Start (Sekunden)"), "1.5");
        await user.selectOptions(screen.getByLabelText("Animation"), "Bounce");
        await user.click(
            screen.getByRole("button", { name: "Alert speichern" }),
        );
        expect(save).toHaveBeenCalledWith(
            expect.objectContaining({
                type: "Special",
                sound_path: "C:/sounds/alert.wav",
                sound_start_seconds: 1.5,
                animation: "Bounce",
                volume_percent: 70,
            }),
        );
    });
    it("loads media and rendered template through the native preview command", async () => {
        invoke.mockResolvedValue({
            text: "Alice folgt jetzt!",
            media: { url: "data:image/png;base64,aGVsbG8=", mime: "image/png" },
            sound: null,
        });
        render(
            <QueryClientProvider client={new QueryClient()}>
                <AlertEditor
                    initial={initial}
                    onSave={() => {}}
                    onClose={() => {}}
                    pending={false}
                />
            </QueryClientProvider>,
        );
        await userEvent
            .setup()
            .click(screen.getByRole("button", { name: "Vorschau laden" }));
        await waitFor(() =>
            expect(invoke).toHaveBeenCalledWith("alert_preview", {
                alert: initial,
                user: "Testnutzer",
            }),
        );
        expect(
            await screen.findByAltText("Alert-Medienvorschau"),
        ).toHaveAttribute("src", "data:image/png;base64,aGVsbG8=");
        expect(screen.getByText("Alice folgt jetzt!")).toBeInTheDocument();
    });
});
