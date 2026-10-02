import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, it, expect, vi } from "vitest";
import { defaultAppSettings } from "../../lib/app-settings";
import { Profiles } from "./Profiles";
const invoke = vi.fn();
const open = vi.fn();
const save = vi.fn();
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
}));
vi.mock("@tauri-apps/plugin-dialog", () => ({
    open: (options: unknown) => open(options),
    save: (options: unknown) => save(options),
}));
function show() {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <Profiles />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    open.mockReset();
    save.mockReset();
    invoke.mockImplementation(async (cmd) =>
        cmd === "list_profiles"
            ? {
                  profiles: [
                      {
                          id: "studio",
                          name: "Studio",
                          description: "About",
                          updatedAt: "2026-01-01T00:00:00Z",
                      },
                  ],
                  warnings: ["broken.json unreadable"],
              }
            : cmd === "get_settings"
              ? defaultAppSettings()
              : null,
    );
});
it("creates profiles, exports through a native picker, and keeps errors visible", async () => {
    const user = userEvent.setup();
    show();
    expect(await screen.findByText("Studio")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Profilname"), "Gaming");
    await user.click(
        screen.getByRole("button", {
            name: "Aus aktuellen Einstellungen erstellen",
        }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("create_profile", {
            name: "Gaming",
            description: "",
        }),
    );
    save.mockResolvedValue("C:/exports/Studio.ccsprofile");
    await user.click(
        screen.getByRole("button", { name: "Profil exportieren" }),
    );
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("export_profile", {
            id: "studio",
            path: "C:/exports/Studio.ccsprofile",
        }),
    );
    expect(screen.getByText("broken.json unreadable")).toBeInTheDocument();
    invoke.mockRejectedValueOnce(new Error("Cannot read import"));
    open.mockResolvedValue("C:/old.ccsprofile");
    await user.click(
        screen.getByRole("button", { name: "Profil importieren" }),
    );
    expect(await screen.findByText(/Cannot read import/)).toBeInTheDocument();
});
it("applies a concrete selected profile with original settings and cancels file dialogs", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    const user = userEvent.setup();
    show();
    await screen.findByText("Studio");
    await user.click(screen.getByRole("button", { name: "Profil anwenden" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("apply_profile", {
            id: "studio",
            original: defaultAppSettings(),
        }),
    );
    open.mockResolvedValue(null);
    const before = invoke.mock.calls.length;
    await user.click(
        screen.getByRole("button", { name: "Profil importieren" }),
    );
    await waitFor(() => expect(open).toHaveBeenCalled());
    expect(
        invoke.mock.calls
            .slice(before)
            .some(([cmd]) => cmd === "import_profile"),
    ).toBe(false);
    confirm.mockRestore();
});
it("edits metadata and removes a profile only after confirmation", async () => {
    const user = userEvent.setup();
    show();
    await screen.findByText("Studio");
    await user.click(screen.getByRole("button", { name: "Profil bearbeiten" }));
    expect(screen.getByLabelText("Profilname")).toHaveValue("Studio");
    fireEvent.change(screen.getByLabelText("Profilname"), {
        target: { value: "Renamed" },
    });
    await user.click(screen.getByRole("button", { name: "Profil speichern" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("update_profile", {
            id: "studio",
            name: "Renamed",
            description: "About",
        }),
    );
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await user.click(screen.getByRole("button", { name: "Profil löschen" }));
    expect(invoke.mock.calls.some(([cmd]) => cmd === "delete_profile")).toBe(
        false,
    );
    confirm.mockReturnValue(true);
    await user.click(screen.getByRole("button", { name: "Profil löschen" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("delete_profile", { id: "studio" }),
    );
    confirm.mockRestore();
});
