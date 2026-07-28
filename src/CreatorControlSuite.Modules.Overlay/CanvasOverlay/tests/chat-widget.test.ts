// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import {
  appendChatMessage,
  appendChatEvent,
  clearChat,
  createChatEl,
  removeChatMessageById,
  removeChatMessagesByUser,
  resolveChatUserColor,
  updateChat,
  CHAT_VARIANTS,
  TWITCH_DEFAULT_COLORS
} from "../src/shared/widgets/chat";
import type { LayoutItem } from "../src/shared/types";

function chatItem(props: Record<string, unknown> = {}): LayoutItem {
  return {
    id: "chat-1",
    kind: "widget",
    type: "chat",
    x: 0,
    y: 0,
    w: 420,
    h: 560,
    z: 1,
    props: {
      maxLines: 10,
      variant: "classic",
      ...props
    }
  };
}

function msg(overrides: Record<string, unknown> = {}) {
  return {
    messageId: "m1",
    userName: "PixelFox",
    userLogin: "pixelfox",
    userId: "u1",
    color: "#FF0000",
    badges: "[]",
    parts: JSON.stringify([{ type: "text", text: "hello" }]),
    at: "2026-07-28T12:34:56Z",
    ...overrides
  };
}

describe("resolveChatUserColor", () => {
  it("uses valid twitch hex colors", () => {
    expect(resolveChatUserColor("#ff0000", "alice")).toBe("#FF0000");
    expect(resolveChatUserColor("  #00FF00  ", "alice")).toBe("#00FF00");
  });

  it("falls back to twitch default palette for empty color", () => {
    const login = "pixelfox";
    const n = login.charCodeAt(0) + login.charCodeAt(login.length - 1);
    const expected = TWITCH_DEFAULT_COLORS[n % TWITCH_DEFAULT_COLORS.length];
    expect(resolveChatUserColor("", login)).toBe(expected);
    expect(resolveChatUserColor("", login)).toBe(resolveChatUserColor(null, login));
  });

  it("uses fallback when twitch colors disabled", () => {
    expect(resolveChatUserColor("#FF0000", "alice", {
      useTwitchUserColor: false,
      fallbackUserColor: "#abcdef"
    })).toBe("#ABCDEF");
  });
});

describe("chat widget", () => {
  it("applies variant class and style css vars", () => {
    const el = createChatEl(chatItem({
      variant: "bubbles",
      messageColor: "#112233",
      eventColor: "#445566",
      fontSizePx: 22,
      fontFamily: "Comic Sans MS",
      fontWeight: "700",
      lineHeight: 1.5
    }));
    expect(el.classList.contains("ccs-chat-v-bubbles")).toBe(true);
    expect(CHAT_VARIANTS).toContain("bubbles");
    expect(el.style.getPropertyValue("--ccs-chat-message-color")).toBe("#112233");
    expect(el.style.getPropertyValue("--ccs-chat-event-color")).toBe("#445566");
    expect(el.style.getPropertyValue("--ccs-chat-font-size")).toBe("22px");
    expect(el.style.getPropertyValue("--ccs-chat-font-family")).toContain("Comic Sans MS");
    expect(el.style.getPropertyValue("--ccs-chat-font-weight")).toBe("700");
    expect(el.style.getPropertyValue("--ccs-chat-line-height")).toBe("1.5");
  });

  it("renders twitch color on username and separators", () => {
    const el = createChatEl(chatItem({ separator: "dash" }));
    appendChatMessage(el, msg());
    const user = el.querySelector(".ccs-chat-user") as HTMLElement;
    expect(user.getAttribute("style") || "").toMatch(/color:\s*#FF0000/i);
    expect(user.textContent).toBe("PixelFox -");
  });

  it("hides badges and emotes when toggled off", () => {
    const el = createChatEl(chatItem({ showBadges: false, showEmotes: false }));
    appendChatMessage(el, msg({
      badges: JSON.stringify([{ setId: "mod", url: "https://cdn/mod", title: "Mod" }]),
      parts: JSON.stringify([
        { type: "emote", text: "Kappa", url: "https://cdn/kappa" },
        { type: "text", text: " hi" }
      ])
    }));
    expect(el.querySelector(".ccs-chat-badge")).toBeNull();
    expect(el.querySelector(".ccs-chat-emote")).toBeNull();
    expect(el.querySelector(".ccs-chat-message")?.textContent).toContain("Kappa");
  });

  it("shows timestamps and nameDisplay login", () => {
    const el = createChatEl(chatItem({
      showTimestamps: true,
      timestampFormat: "hh:mm:ss",
      nameDisplay: "login"
    }));
    appendChatMessage(el, msg());
    expect(el.querySelector(".ccs-chat-time")?.textContent).toMatch(/\d{2}:\d{2}:\d{2}/);
    expect(el.querySelector(".ccs-chat-user")?.textContent).toContain("pixelfox");
  });

  it("skips command messages when hideCommands is on", () => {
    const el = createChatEl(chatItem({ hideCommands: true }));
    expect(appendChatMessage(el, msg({
      messageId: "cmd",
      parts: JSON.stringify([{ type: "text", text: "!song" }])
    }))).toBe(false);
    expect(el.querySelectorAll(".ccs-chat-line:not(.ccs-chat-status)")).toHaveLength(0);
  });

  it("removes messages by id and user", () => {
    const el = createChatEl(chatItem());
    appendChatMessage(el, msg({ messageId: "a", userLogin: "alice", userId: "1" }));
    appendChatMessage(el, msg({ messageId: "b", userLogin: "bob", userId: "2", userName: "Bob" }));
    expect(removeChatMessageById(el, "a")).toBe(true);
    expect(el.querySelector('[data-message-id="a"]')).toBeNull();
    expect(removeChatMessagesByUser(el, "bob", "2")).toBe(1);
    expect(el.querySelectorAll(".ccs-chat-line:not(.ccs-chat-status)")).toHaveLength(0);
  });

  it("clearChat resets lines and seen ids", () => {
    const el = createChatEl(chatItem({ showStatusLine: true }));
    appendChatMessage(el, msg());
    clearChat(el);
    expect(el.querySelector(".ccs-chat-status")).not.toBeNull();
    expect(appendChatMessage(el, msg())).toBe(true);
  });

  it("respects showEventIcons false", () => {
    const el = createChatEl(chatItem({ showEventIcons: false }));
    updateChat(el, chatItem({ showEventIcons: false }), null);
    appendChatEvent(el, { type: "channel.follow", summary: "followed" });
    expect(el.querySelector(".ccs-chat-event-icon")).toBeNull();
    expect(el.querySelector(".ccs-chat-event-summary")?.textContent).toBe("followed");
  });
});
