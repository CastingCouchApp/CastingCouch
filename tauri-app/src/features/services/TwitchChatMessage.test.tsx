import { render, screen } from "@testing-library/react";
import { expect, it } from "vitest";
import { TwitchChatMessage } from "./TwitchChatMessage";
it("renders enriched Twitch and third-party emotes, badges and escaped names as React content", () => {
    render(
        <TwitchChatMessage
            data={{
                userName: "<Alice>",
                color: "#aabbcc",
                text: "Hi Kappa Same",
                parts: JSON.stringify([
                    { type: "text", text: "Hi " },
                    {
                        type: "emote",
                        text: "Kappa",
                        url: "https://cdn/twitch",
                        provider: "twitch",
                    },
                    {
                        type: "emote",
                        text: "Same",
                        url: "https://cdn/7tv",
                        provider: "7tv",
                    },
                ]),
                badges: JSON.stringify([
                    { url: "https://cdn/sub", title: "Subscriber" },
                ]),
            }}
        />,
    );
    expect(screen.getByText("<Alice>")).toHaveStyle({ color: "#aabbcc" });
    expect(screen.getByRole("img", { name: "Kappa" })).toHaveAttribute(
        "src",
        "https://cdn/twitch",
    );
    expect(screen.getByRole("img", { name: "Same" })).toHaveAttribute(
        "src",
        "https://cdn/7tv",
    );
    expect(screen.getByRole("img", { name: "Subscriber" })).toHaveAttribute(
        "src",
        "https://cdn/sub",
    );
});
it("falls back to message text for malformed parts and rejects executable image URLs", () => {
    const view = render(
        <TwitchChatMessage
            data={{
                userLogin: "alice",
                text: "Readable",
                parts: "not-json",
                badges: "{}",
                color: "url(bad)",
            }}
        />,
    );
    expect(screen.getByText("Readable")).toBeInTheDocument();
    expect(screen.getByText("alice").style.color).toBe("");
    view.rerender(
        <TwitchChatMessage
            data={{
                userName: "Alice",
                text: "Bad",
                parts: JSON.stringify([
                    { type: "emote", text: "Bad", url: "javascript:alert(1)" },
                ]),
                badges: JSON.stringify([
                    { url: "data:image/svg+xml,bad", title: "Unsafe" },
                ]),
            }}
        />,
    );
    expect(screen.getByText("Bad")).toBeInTheDocument();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
});
