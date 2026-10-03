import { act, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { AppCloseNotice } from "./AppCloseNotice";
let receive: (event: { payload: string }) => void = () => {};
const stop = vi.fn();
vi.mock("@tauri-apps/api/event", () => ({
    listen: async (_name: string, fn: typeof receive) => {
        receive = fn;
        return stop;
    },
}));
it("shows a native close failure and releases the listener on unmount", async () => {
    Object.defineProperty(window, "__TAURI_INTERNALS__", {
        value: {},
        configurable: true,
    });
    const view = render(<AppCloseNotice />);
    await act(async () => {});
    act(() => receive({ payload: "Raid ist noch unaufgelöst" }));
    expect(screen.getByRole("alert")).toHaveTextContent(
        "Raid ist noch unaufgelöst",
    );
    view.unmount();
    expect(stop).toHaveBeenCalledTimes(1);
    delete (window as any).__TAURI_INTERNALS__;
});
