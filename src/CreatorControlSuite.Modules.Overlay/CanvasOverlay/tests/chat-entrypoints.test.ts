import { buildSync } from "esbuild";
import { createServer } from "node:http";
import { JSDOM } from "jsdom";
import { expect, it, vi } from "vitest";

const runtime = buildSync({
    entryPoints: ["src/shared/index.ts"],
    bundle: true,
    write: false,
    format: "iife",
    loader: { ".css": "empty" },
}).outputFiles[0].text;
const entries = Object.fromEntries(
    ["view", "solo"].map((name) => [
        name,
        buildSync({
            entryPoints: [`src/${name}/main.ts`],
            bundle: true,
            write: false,
            format: "iife",
        }).outputFiles[0].text,
    ]),
);

for (const mode of ["view", "solo"])
    it(`boots the compiled ${mode} entrypoint with HTTP config/history and applies live changes`, async () => {
        let config = {
            enabled: true,
            fontSizePx: 24,
            backgroundType: "Color",
            backgroundColor: "#123456",
        };
        const message = {
            source: "twitch",
            type: "channel.chat.message",
            data: {
                messageId: "saved",
                userName: "Viewer",
                parts: JSON.stringify([
                    {
                        type: "emote",
                        text: "Kappa",
                        url: "https://cdn.example/emote.png",
                    },
                ]),
                badges: JSON.stringify([
                    { url: "https://cdn.example/badge.png", title: "Mod" },
                ]),
            },
        };
        const requests: string[] = [];
        const server = createServer((req, res) => {
            requests.push(req.url || "");
            res.setHeader("content-type", "application/json");
            if (req.url === "/extensions")
                res.end(JSON.stringify({ packs: [] }));
            else if (req.url === "/chat/config")
                res.end(JSON.stringify(config));
            else if (req.url === "/chat/history")
                res.end(JSON.stringify({ events: [message] }));
            else if (req.url === "/layout/default")
                res.end(
                    JSON.stringify({
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
                        ],
                    }),
                );
            else res.end("{}");
        });
        await new Promise<void>((resolve) =>
            server.listen(0, "127.0.0.1", resolve),
        );
        const address = server.address();
        if (!address || typeof address === "string")
            throw Error("Missing port");
        const base = `http://127.0.0.1:${address.port}`;
        // Solo defaults are intentional widget overrides; explicit props also survive reloads.
        const path =
            mode === "view"
                ? "/view/default"
                : `/w/chat?props=${encodeURIComponent(JSON.stringify({ fontSizePx: 31 }))}`;
        const dom = new JSDOM('<div id="root"></div>', {
            url: base + path,
            runScripts: "outside-only",
        });
        let socket: InstanceType<typeof dom.window.EventTarget>;
        class Socket extends dom.window.EventTarget {
            constructor() {
                super();
                socket = this;
                queueMicrotask(() =>
                    this.dispatchEvent(new dom.window.Event("open")),
                );
            }
            close() {}
        }
        Object.assign(dom.window, {
            WebSocket: Socket,
            fetch: (url: string, options: RequestInit) =>
                fetch(new URL(url, base), options),
        });
        const send = (event: unknown) =>
            socket.dispatchEvent(
                new dom.window.MessageEvent("message", {
                    data: JSON.stringify(event),
                }),
            );
        try {
            dom.window.eval(runtime);
            dom.window.eval(entries[mode]);
            await vi.waitFor(() =>
                expect(
                    dom.window.document.querySelector(
                        '[data-message-id="saved"]',
                    ),
                ).not.toBeNull(),
            );
            expect(requests).toContain("/chat/config");
            expect(requests).toContain("/chat/history");
            expect(
                dom.window.document
                    .querySelector(".ccs-chat-emote")
                    ?.getAttribute("src"),
            ).toBe("https://cdn.example/emote.png");
            expect(
                dom.window.document
                    .querySelector(".ccs-chat-badge")
                    ?.getAttribute("src"),
            ).toBe("https://cdn.example/badge.png");
            const chat =
                dom.window.document.querySelector<HTMLElement>(".ccs-chat")!;
            expect(chat.style.getPropertyValue("--ccs-chat-font-size")).toBe(
                mode === "view" ? "24px" : "31px",
            );
            config = { ...config, fontSizePx: 28, enabled: false };
            send({ source: "app", type: "app.chat.config" });
            await vi.waitFor(() => expect(chat.hidden).toBe(true));
            send({
                source: "twitch",
                type: "channel.chat.message_delete",
                data: { messageId: "saved" },
            });
            config = { ...config, enabled: true };
            send({ source: "app", type: "app.chat.config" });
            await vi.waitFor(() => expect(chat.hidden).toBe(false));
            expect(chat.querySelector('[data-message-id="saved"]')).toBeNull();
            expect(chat.style.getPropertyValue("--ccs-chat-font-size")).toBe(
                mode === "view" ? "28px" : "31px",
            );
        } finally {
            dom.window.close();
            server.closeAllConnections();
            await new Promise<void>((resolve) => server.close(() => resolve()));
        }
    });
