// @vitest-environment jsdom
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { beforeEach, afterEach, expect, it, vi } from "vitest";

const code = readFileSync(
    resolve(process.cwd(), "../ChatOverlay/chat.js"),
    "utf8",
);
let socket: EventTarget;
let config: Record<string, unknown>;
let history: unknown[];
const message = (id: string) => ({
    source: "twitch",
    type: "channel.chat.message",
    data: {
        messageId: id,
        userName: "Viewer",
        parts: JSON.stringify([{ type: "text", text: id }]),
    },
});
function send(payload: unknown) {
    socket.dispatchEvent(
        new MessageEvent("message", { data: JSON.stringify(payload) }),
    );
}
beforeEach(() => {
    document.body.innerHTML = '<div id="panel"><div id="chat"></div></div>';
    config = { enabled: true, fontSizePx: 18, backgroundType: "Color" };
    history = [message("history")];
    vi.stubGlobal(
        "WebSocket",
        class extends EventTarget {
            constructor() {
                super();
                socket = this;
            }
        },
    );
    vi.stubGlobal(
        "fetch",
        vi.fn(async (url: string) => ({
            ok: true,
            json: async () =>
                url === "/chat/config" ? config : { events: history },
        })),
    );
    new Function(code)();
    socket.dispatchEvent(new Event("open"));
});
afterEach(() => {
    vi.unstubAllGlobals();
});

it("updates the actual standalone script without reconnect and applies disabled/events/background state", async () => {
    await vi.waitFor(() =>
        expect(
            document.querySelector('[data-message-id="history"]'),
        ).not.toBeNull(),
    );
    send({ source: "twitch", type: "channel.follow", summary: "follow" });
    expect(document.querySelector(".event")).not.toBeNull();
    config = {
        enabled: true,
        fontSizePx: 28,
        showTwitchEvents: false,
        backgroundType: "Image",
        backgroundVersion: "2",
        backgroundOpacity: 0.3,
    };
    send({ source: "app", type: "app.chat.config" });
    await vi.waitFor(() =>
        expect(
            document
                .getElementById("panel")
                ?.style.getPropertyValue("--chat-font-size"),
        ).toBe("28px"),
    );
    expect(document.querySelector(".event")).toBeNull();
    expect(
        document
            .getElementById("panel")
            ?.style.getPropertyValue("--chat-bg-image"),
    ).toContain("v=2");
    config = { enabled: false };
    send({ source: "app", type: "app.chat.config" });
    await vi.waitFor(() =>
        expect(document.getElementById("panel")?.hidden).toBe(true),
    );
    send(message("disabled"));
    expect(document.querySelector('[data-message-id="disabled"]')).toBeNull();
    send({
        source: "twitch",
        type: "channel.chat.message_delete",
        data: { messageId: "history" },
    });
    history = [message("after")];
    config = { enabled: true, backgroundType: "None" };
    send({ source: "app", type: "app.chat.config" });
    await vi.waitFor(() =>
        expect(
            document.querySelector('[data-message-id="after"]'),
        ).not.toBeNull(),
    );
    expect(document.getElementById("panel")?.hidden).toBe(false);
    expect(document.querySelector('[data-message-id="history"]')).toBeNull();
    expect(
        document
            .getElementById("panel")
            ?.style.getPropertyValue("--chat-bg-image"),
    ).toBe("");
});

it("keeps the last working appearance after a failed reload", async () => {
    await vi.waitFor(() =>
        expect(
            document
                .getElementById("panel")
                ?.style.getPropertyValue("--chat-font-size"),
        ).toBe("18px"),
    );
    vi.mocked(fetch).mockRejectedValueOnce(new Error("offline"));
    send({ source: "app", type: "app.chat.config" });
    await Promise.resolve();
    await Promise.resolve();
    expect(
        document
            .getElementById("panel")
            ?.style.getPropertyValue("--chat-font-size"),
    ).toBe("18px");
});

it("does not resurrect a moderated message from a delayed history response", async () => {
    await vi.waitFor(() =>
        expect(
            document.querySelector('[data-message-id="history"]'),
        ).not.toBeNull(),
    );
    let resolve!: (value: unknown) => void;
    vi.mocked(fetch)
        .mockImplementationOnce(
            async () => ({ ok: true, json: async () => config }) as Response,
        )
        .mockImplementationOnce(
            () =>
                new Promise((r) => {
                    resolve = r;
                }),
        );
    send({ source: "app", type: "app.chat.config" });
    await vi.waitFor(() => expect(resolve).toBeTypeOf("function"));
    send({
        source: "twitch",
        type: "channel.chat.message_delete",
        data: { messageId: "history" },
    });
    resolve({ ok: true, json: async () => ({ events: [message("history")] }) });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    expect(document.querySelector('[data-message-id="history"]')).toBeNull();
});
