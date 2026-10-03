import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
const script = readFileSync(
    resolve(
        "../src/CreatorControlSuite.Modules.YouTubeMusic/Assets/ytmusic-bridge.js",
    ),
    "utf8",
).replace(/__CCS_BRIDGE_PORT__/g, "43900");
type Bridge = { stop: () => void; isConnected: () => boolean; baseUrl: string };
const bridge = () =>
    (window as unknown as { __ccsYtMusicBridge?: Bridge }).__ccsYtMusicBridge;
beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(console, "log").mockImplementation(() => {});
});
afterEach(() => {
    bridge()?.stop();
    document.body.innerHTML = "";
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
});
it("runs the shared inline bookmarklet, posts metadata/progress and executes the native command contract", async () => {
    document.body.innerHTML =
        '<ytmusic-player-bar><span class="title">Song</span><span class="byline">Artist · Album</span><img id="song-image"/><div id="progress-bar" value="12" aria-valuemax="120"></div><button id="play-pause-button" title="Pausieren"></button><button class="next-button"></button><button class="previous-button"></button></ytmusic-player-bar>';
    const play = vi.fn(),
        next = vi.fn(),
        previous = vi.fn();
    document
        .querySelector("#play-pause-button")!
        .addEventListener("click", play);
    document.querySelector(".next-button")!.addEventListener("click", next);
    document
        .querySelector(".previous-button")!
        .addEventListener("click", previous);
    Object.defineProperty(navigator, "mediaSession", {
        configurable: true,
        value: {
            metadata: {
                title: "Fallback",
                artist: "Fallback Artist",
                album: "Fallback Album",
                artwork: [
                    { src: "https://example.com/cover.jpg", sizes: "500x500" },
                ],
            },
        },
    });
    let commands = ["next", "previous", "pause", "playpause", "play"];
    const fetch = vi.fn(async (url: string, _options?: RequestInit) => {
        if (url.endsWith("/commands")) {
            const response = commands;
            commands = [];
            return new Response(JSON.stringify({ commands: response }));
        }
        return new Response("{}");
    });
    vi.stubGlobal("fetch", fetch);
    window.eval(script);
    await vi.advanceTimersByTimeAsync(1);
    expect(bridge()?.baseUrl).toBe("http://127.0.0.1:43900/ytmusic");
    expect(bridge()?.isConnected()).toBe(true);
    const state = fetch.mock.calls.find(([url]) => url.endsWith("/state"));
    expect(state?.[1]?.method).toBe("POST");
    expect(state?.[1]?.mode).toBe("cors");
    expect(JSON.parse(String(state?.[1]?.body))).toEqual({
        title: "Song",
        artist: "Artist",
        album: "Album",
        coverUrl: "https://example.com/cover.jpg",
        isPlaying: true,
        progressMs: 12000,
        durationMs: 120000,
    });
    expect(next).toHaveBeenCalledOnce();
    expect(previous).toHaveBeenCalledOnce();
    expect(play).toHaveBeenCalledTimes(3);
    bridge()?.stop();
    const count = fetch.mock.calls.length;
    await vi.advanceTimersByTimeAsync(10000);
    expect(fetch).toHaveBeenCalledTimes(count);
});
it("reconnects after browser network failures and replaces a previously running bookmarklet", async () => {
    let offline = true;
    const fetch = vi.fn(async (url: string) => {
        if (offline) throw new Error("offline");
        return new Response(
            url.endsWith("/commands") ? ' {"commands":[]}' : "{}",
        );
    });
    vi.stubGlobal("fetch", fetch);
    window.eval(script);
    await vi.advanceTimersByTimeAsync(1);
    expect(bridge()?.isConnected()).toBe(false);
    offline = false;
    window.dispatchEvent(new Event("online"));
    await vi.advanceTimersByTimeAsync(1);
    expect(bridge()?.isConnected()).toBe(true);
    const previous = bridge()!;
    window.eval(script);
    await vi.advanceTimersByTimeAsync(1);
    expect(previous.isConnected()).toBe(false);
    expect(bridge()?.isConnected()).toBe(true);
});
