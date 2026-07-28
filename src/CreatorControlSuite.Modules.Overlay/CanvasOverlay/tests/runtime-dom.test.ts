// @vitest-environment jsdom

import { describe, expect, it } from "vitest";
import { createTextEl, updateText } from "../src/shared/runtime/core-functions";
import {
  createSocialsEl
} from "../src/shared/widgets/socials";
import {
  appendChatMessage,
  createChatEl
} from "../src/shared/widgets/chat";
import type { LayoutItem } from "../src/shared/types";

function textItem(content: string): LayoutItem {
  return {
    id: "text-1",
    kind: "widget",
    type: "text",
    x: 0,
    y: 0,
    w: 400,
    h: 120,
    z: 1,
    props: {
      content,
      align: "left",
      verticalAlign: "top",
      color: "#ff5500"
    }
  };
}

describe("runtime DOM smoke", () => {
  it("creates and updates text widgets without interpreting markup", () => {
    const element = createTextEl(textItem("<script>alert(1)</script>"));

    expect(element.className).toBe("ccs-text");
    expect(element.querySelector("script")).toBeNull();
    expect(element.textContent).toBe("<script>alert(1)</script>");

    updateText(element, textItem("Live"));

    expect(element.textContent).toBe("Live");
    expect(element.style.getPropertyValue("--ccs-text-color")).toBe("#ff5500");
    expect(element.style.getPropertyValue("--ccs-text-justify")).toBe("flex-start");
  });

  it("renders socials from the extracted module with escaped content", () => {
    const item: LayoutItem = {
      ...textItem(""),
      id: "socials-1",
      type: "socials",
      props: {
        platform: "twitch",
        label: "<img src=x onerror=alert(1)>",
        handle: "<script>bad()</script>",
        url: "https://twitch.tv/example"
      }
    };

    const element = createSocialsEl(item);

    expect(element.querySelector(".ccs-socials-label")?.textContent)
      .toBe("<img src=x onerror=alert(1)>");
    expect(element.querySelector(".ccs-socials-handle")?.textContent)
      .toBe("@<script>bad()</script>");
    expect(element.querySelector(".ccs-socials-label img")).toBeNull();
    expect(element.querySelector(".ccs-socials-handle script")).toBeNull();
  });

  it("deduplicates and escapes chat messages in the extracted module", () => {
    const item: LayoutItem = {
      ...textItem(""),
      id: "chat-1",
      type: "chat",
      props: { maxLines: 10 }
    };
    const element = createChatEl(item);
    const message = {
      messageId: "message-1",
      userName: "<b>viewer</b>",
      color: "#123456",
      badges: "[]",
      parts: JSON.stringify([
        { type: "text", text: "<script>bad()</script>" }
      ])
    };

    expect(appendChatMessage(element, message)).toBe(true);
    expect(appendChatMessage(element, message)).toBe(false);

    expect(element.querySelectorAll(".ccs-chat-line")).toHaveLength(1);
    expect(element.querySelector(".ccs-chat-user")?.textContent)
      .toBe("<b>viewer</b>:");
    expect(element.querySelector(".ccs-chat-message")?.textContent)
      .toBe("<script>bad()</script>");
    expect(element.querySelector("script")).toBeNull();
  });
});
