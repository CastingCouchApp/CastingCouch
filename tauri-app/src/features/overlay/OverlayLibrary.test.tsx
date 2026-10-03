import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { OverlayLibrary } from "./OverlayLibrary";

const fetchMock = vi.fn();
const invoke = vi.fn(),
    listen = vi.fn(),
    unlisten = vi.fn(),
    open = vi.fn();
vi.mock("../../lib/api", async (original) => ({
    ...(await original<typeof import("../../lib/api")>()),
    tauriInvoke: (cmd: string, args: unknown) => invoke(cmd, args),
    listenExtensionPacksChanged: (handler: unknown) => listen(handler),
}));
vi.mock("@tauri-apps/plugin-dialog", () => ({
    open: (options: unknown) => open(options),
}));
let version = "1.0";
let installed = true;
const pack = () => ({
    id: "cool-kit",
    name: "Cool Kit",
    version,
    widgets: [{}],
    effects: [{}],
    animations: [{}],
    fonts: [{}],
    assets: [{}],
});
function show() {
    return render(
        <QueryClientProvider
            client={
                new QueryClient({
                    defaultOptions: { queries: { retry: false } },
                })
            }
        >
            <OverlayLibrary baseUrl="http://127.0.0.1:8765" />
        </QueryClientProvider>,
    );
}
beforeEach(() => {
    invoke.mockReset();
    listen.mockReset().mockImplementation(async () => unlisten);
    unlisten.mockClear();
    open.mockReset().mockResolvedValue(null);
    version = "1.0";
    installed = true;
    fetchMock
        .mockReset()
        .mockImplementation(async (url: string, options?: RequestInit) => {
            if (options?.method === "POST") {
                version = "2.0";
                return { ok: true, json: async () => pack() };
            }
            if (options?.method === "DELETE") {
                installed = false;
                return { ok: true, json: async () => ({ ok: true }) };
            }
            return {
                ok: true,
                json: async () =>
                    url.endsWith("/extensions")
                        ? { packs: installed ? [pack()] : [] }
                        : { assets: [] },
            };
        });
    vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => {
    vi.unstubAllGlobals();
    delete (window as unknown as Record<string, unknown>).__TAURI_INTERNALS__;
    vi.restoreAllMocks();
});

it("uses native pack commands, handles picker cancellation and refreshes on native events", async () => {
    Object.assign(window, { __TAURI_INTERNALS__: {} });
    invoke.mockImplementation(async (cmd) => {
        if (cmd === "list_extension_packs") return installed ? [pack()] : [];
        if (cmd === "import_extension_pack") {
            version = "2.0";
            return pack();
        }
        if (cmd === "uninstall_extension_pack") {
            installed = false;
            return null;
        }
        throw Error(cmd);
    });
    const view = show();
    await screen.findByText(/Cool Kit · 1.0/);
    expect(invoke).toHaveBeenCalledWith("list_extension_packs", undefined);
    open.mockResolvedValueOnce("D:/Packs/cool-kit.zip");
    fireEvent.click(screen.getByRole("button", { name: "ZIP importieren" }));
    await screen.findByText(/Cool Kit · 2.0/);
    expect(invoke).toHaveBeenCalledWith("import_extension_pack", {
        path: "D:/Packs/cool-kit.zip",
    });
    version = "3.0";
    listen.mock.calls[0][0]({ action: "installed", packId: "cool-kit" });
    await screen.findByText(/Cool Kit · 3.0/);
    fireEvent.click(screen.getByRole("button", { name: "ZIP importieren" }));
    await waitFor(() => expect(open).toHaveBeenCalledTimes(2));
    expect(
        invoke.mock.calls.filter(([cmd]) => cmd === "import_extension_pack"),
    ).toHaveLength(1);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: "Deinstallieren" }));
    await waitFor(() =>
        expect(invoke).toHaveBeenCalledWith("uninstall_extension_pack", {
            id: "cool-kit",
        }),
    );
    expect(fetchMock.mock.calls.filter(([, o]) => o?.method)).toHaveLength(0);
    view.unmount();
    expect(unlisten).toHaveBeenCalledOnce();
});

it("reports an invalid catalog response without crashing the overlay page", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ ok: true }) });
    show();
    expect(
        await screen.findByText(/Ungültiger Extension-Pack-Katalog/),
    ).toBeInTheDocument();
    expect(
        screen.getByRole("button", { name: "Packs aktualisieren" }),
    ).toBeInTheDocument();
    expect(
        screen.queryByText("0 Extension Packs installiert"),
    ).not.toBeInTheDocument();
});

it("shows pack content, imports an update and confirms or cancels uninstall", async () => {
    show();
    expect(await screen.findByText(/Cool Kit · 1.0/)).toBeInTheDocument();
    expect(
        screen.getByText(/1 Widget.*1 Effekt.*1 Animation.*1 Font.*1 Asset/),
    ).toBeInTheDocument();
    const file = new File(["zip"], "cool-kit.zip", { type: "application/zip" });
    fireEvent.change(screen.getByLabelText("ZIP importieren"), {
        target: { files: [file] },
    });
    await screen.findByText(/Cool Kit · 2.0/);
    expect(screen.getByRole("status")).toHaveTextContent(
        /Cool Kit.*installiert/,
    );
    const upload = fetchMock.mock.calls.find(([, o]) => o?.method === "POST");
    expect(upload?.[0]).toBe("http://127.0.0.1:8765/extensions/install");
    expect((upload?.[1].body as FormData).get("file")).toHaveProperty(
        "name",
        "cool-kit.zip",
    );
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    fireEvent.click(screen.getByRole("button", { name: "Deinstallieren" }));
    expect(fetchMock.mock.calls.some(([, o]) => o?.method === "DELETE")).toBe(
        false,
    );
    confirm.mockReturnValue(true);
    fireEvent.click(screen.getByRole("button", { name: "Deinstallieren" }));
    await waitFor(() =>
        expect(screen.queryByText(/Cool Kit · 2.0/)).not.toBeInTheDocument(),
    );
    expect(screen.getByRole("status")).toHaveTextContent("deinstalliert");
    confirm.mockRestore();
});

it("retains the installed version on a rejected update and allows catalog retry", async () => {
    let unavailable = false;
    fetchMock.mockImplementation(async (url: string, options?: RequestInit) => {
        if (options?.method === "POST")
            return {
                ok: false,
                text: async () => JSON.stringify({ error: "CSS-Datei fehlt" }),
            };
        if (url.endsWith("/extensions") && unavailable)
            throw Error("Server nicht erreichbar");
        return {
            ok: true,
            json: async () =>
                url.endsWith("/extensions")
                    ? { packs: [pack()] }
                    : { assets: [] },
        };
    });
    show();
    await screen.findByText(/Cool Kit · 1.0/);
    fireEvent.change(screen.getByLabelText("ZIP importieren"), {
        target: { files: [new File(["invalid"], "bad.zip")] },
    });
    expect(await screen.findByRole("alert")).toHaveTextContent(
        "CSS-Datei fehlt",
    );
    expect(screen.getByText(/Cool Kit · 1.0/)).toBeInTheDocument();
    unavailable = true;
    fireEvent.click(
        screen.getByRole("button", { name: "Packs aktualisieren" }),
    );
    await screen.findByText(/Server nicht erreichbar/);
    unavailable = false;
    fireEvent.click(
        screen.getByRole("button", { name: "Packs aktualisieren" }),
    );
    await waitFor(() =>
        expect(
            screen.queryByText(/Server nicht erreichbar/),
        ).not.toBeInTheDocument(),
    );
    expect(screen.getByText(/Cool Kit · 1.0/)).toBeInTheDocument();
});
