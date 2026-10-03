// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createRuntime } from "../src/shared/runtime/create-runtime";

const message = (id: string) => ({
    source: "twitch",
    type: "channel.chat.message",
    data: {
        messageId: id,
        userId: "user",
        userName: "Viewer",
        parts: JSON.stringify([{ type: "text", text: id }]),
    },
});
beforeEach(() => {
    vi.useFakeTimers();
    document.body.innerHTML = "";
});
afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
});

function runtime(solo = false) {
    const root = document.createElement("div");
    document.body.append(root);
    const rt = createRuntime({
        root,
        soloType: solo ? "chat" : undefined,
        chatConfig: { enabled: true, fontSizePx: 18 },
        layout: {
            canvasWidth: 1920,
            canvasHeight: 1080,
            items: [
                {
                    id: "chat",
                    kind: "widget",
                    type: "chat",
                    x: 0,
                    y: 0,
                    w: 420,
                    h: 560,
                    z: 1,
                    props: { maxLines: 80 },
                },
                {
                    id: "custom",
                    kind: "widget",
                    type: "chat",
                    x: 420,
                    y: 0,
                    w: 420,
                    h: 560,
                    z: 2,
                    props: { fontSizePx: 32, showTwitchEvents: true },
                },
            ],
        },
    });
    return { root, rt };
}

describe("shared canvas/solo chat config", () => {
    for (const solo of [false, true])
        it(`reloads settings without reconnect and retains widget overrides (solo=${solo})`, async () => {
            const { root, rt } = runtime(solo);
            rt.handleRealtime(message("history"));
            rt.handleRealtime({
                source: "twitch",
                type: "channel.follow",
                summary: "follow",
            });
            const fetchMock = vi
                .fn()
                .mockResolvedValue({
                    ok: true,
                    json: async () => ({
                        enabled: true,
                        showTwitchEvents: false,
                        fontSizePx: 24,
                        paddingPx: 16,
                    }),
                });
            vi.stubGlobal("fetch", fetchMock);
            rt.handleRealtime({
                source: "app",
                type: "app.chat.config",
                data: {},
            });
            await vi.waitFor(() =>
                expect(
                    root
                        .querySelector(".ccs-chat")
                        ?.style.getPropertyValue("--ccs-chat-font-size"),
                ).toBe("24px"),
            );
            expect(fetchMock).toHaveBeenCalledWith("/chat/config", {
                cache: "no-store",
            });
            const chats = root.querySelectorAll<HTMLElement>(".ccs-chat");
            expect(chats[0].querySelector(".ccs-chat-event")).toBeNull();
            expect(
                chats[1].style.getPropertyValue("--ccs-chat-font-size"),
            ).toBe("32px");
            expect(chats[1].querySelector(".ccs-chat-event")).not.toBeNull();
            expect(
                chats[0].querySelector('[data-message-id="history"]'),
            ).not.toBeNull();
        });

    it("hides disabled chat, keeps moderation effective and restores only retained messages", () => {
        const { root, rt } = runtime();
        rt.handleRealtime(message("removed"));
        rt.setChatConfig({ enabled: false });
        expect(root.querySelector<HTMLElement>(".ccs-chat")?.hidden).toBe(true);
        rt.handleRealtime(message("retained"));
        rt.handleRealtime({
            source: "twitch",
            type: "channel.chat.message_delete",
            data: { messageId: "removed" },
        });
        rt.setChatConfig({ enabled: true });
        expect(root.querySelector<HTMLElement>(".ccs-chat")?.hidden).toBe(
            false,
        );
        expect(root.querySelector('[data-message-id="removed"]')).toBeNull();
        expect(
            root.querySelector('[data-message-id="retained"]'),
        ).not.toBeNull();
        rt.handleRealtime({ source: "app", type: "app.chat.clear" });
        rt.setChatConfig({ enabled: true, fontSizePx: 24 });
        expect(root.querySelector('[data-message-id="retained"]')).toBeNull();
    });

    it("ignores a stale config response and retains the last valid config on HTTP errors", async () => {
        const { root, rt } = runtime();
        let resolve!: (value: unknown) => void;
        const fetchMock = vi
            .fn()
            .mockImplementationOnce(
                () =>
                    new Promise((r) => {
                        resolve = r;
                    }),
            )
            .mockResolvedValueOnce({
                ok: true,
                json: async () => ({ fontSizePx: 28 }),
            })
            .mockResolvedValueOnce({ ok: false, status: 500 });
        vi.stubGlobal("fetch", fetchMock);
        rt.handleRealtime({ source: "app", type: "app.chat.config" });
        rt.handleRealtime({ source: "app", type: "app.chat.config" });
        await vi.waitFor(() =>
            expect(
                root
                    .querySelector(".ccs-chat")
                    ?.style.getPropertyValue("--ccs-chat-font-size"),
            ).toBe("28px"),
        );
        resolve({ ok: true, json: async () => ({ fontSizePx: 10 }) });
        await Promise.resolve();
        await Promise.resolve();
        rt.handleRealtime({ source: "app", type: "app.chat.config" });
        await Promise.resolve();
        await Promise.resolve();
        expect(
            root
                .querySelector(".ccs-chat")
                ?.style.getPropertyValue("--ccs-chat-font-size"),
        ).toBe("28px");
    });

    it("does not resurrect deleted messages from a history request started before moderation", async () => {
        const { root, rt } = runtime();
        let resolve!: (value: unknown) => void;
        vi.stubGlobal(
            "fetch",
            vi.fn(
                () =>
                    new Promise((r) => {
                        resolve = r;
                    }),
            ),
        );
        const loading = rt.loadChatHistory();
        rt.handleRealtime(message("removed"));
        rt.handleRealtime({
            source: "twitch",
            type: "channel.chat.message_delete",
            data: { messageId: "removed" },
        });
        resolve({
            ok: true,
            json: async () => ({ events: [message("removed")] }),
        });
        await loading;
        expect(root.querySelector('[data-message-id="removed"]')).toBeNull();
        rt.setLayout(rt.getLayout());
        expect(root.querySelector('[data-message-id="removed"]')).toBeNull();
    });
});
