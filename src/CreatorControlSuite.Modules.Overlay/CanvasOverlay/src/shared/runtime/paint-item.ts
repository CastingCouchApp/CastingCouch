import type { LayoutItem } from "../types";
import { getRegisteredWidget } from "../extensions/registry";
import {
  updateOnline,
  updateSpotify,
  updateChat,
  updateEndingStats,
  updateText,
  updateImage,
  updateCountdown,
  updateSocials,
  updatePartnerRoulette,
  updateGoalBar,
  updateEventTicker,
  pushEventTickerItem,
  updateViewerCount,
  updateLowerThird,
  updateQrCode,
  updateBrbPanel,
  updateAnnouncementBar,
  updateBubatzCantina,
  updateFruppisLandadel,
  updateAnimatedBackground,
  updateDivider,
  updateCamRing,
  updateSticker,
  enqueueAlert,
  appendChatMessage
} from "./core-functions";

export interface PaintItemOptions {
  /** Seed chat/alert/ticker with sample content (palette hover preview). */
  seedDemo?: boolean;
  demoChatMessages?: Array<Record<string, unknown>>;
  demoAlert?: Record<string, unknown>;
  demoTickerEvents?: Array<{ type: string; text?: string; user?: string }>;
}

/** Apply live overlay data (and optional demo seeds) to an already created content element. */
export function paintItemContent(
  el: HTMLElement,
  item: LayoutItem,
  data?: Record<string, unknown> | null,
  chatConfig?: unknown,
  options?: PaintItemOptions
): void {
  if (!el || !item) return;
  const type = item.type;
  const payload = data || {};

  const custom = getRegisteredWidget(type);
  if (custom?.update) {
    try {
      custom.update(el, item, payload, chatConfig);
    } catch {
      /* ignore pack update errors */
    }
    if (options?.seedDemo) {
      seedDemoContent(el, item, options);
    }
    return;
  }

  if (type === "online") updateOnline(el, item, payload);
  else if (type === "music" || type === "spotify") updateSpotify(el, item, payload);
  else if (type === "chat") updateChat(el, item, chatConfig);
  else if (type === "ending-stats") updateEndingStats(el, item, payload);
  else if (type === "text") updateText(el, item);
  else if (type === "image") updateImage(el, item);
  else if (type === "countdown") updateCountdown(el, item, payload);
  else if (type === "socials") updateSocials(el, item);
  else if (type === "partner-roulette") updatePartnerRoulette(el, item);
  else if (type === "goal-bar") updateGoalBar(el, item, payload);
  else if (type === "event-ticker") updateEventTicker(el, item, payload);
  else if (type === "viewer-count") updateViewerCount(el, item, payload);
  else if (type === "lower-third") updateLowerThird(el, item);
  else if (type === "qr-code") updateQrCode(el, item);
  else if (type === "brb-panel") updateBrbPanel(el, item, payload);
  else if (type === "announcement-bar") updateAnnouncementBar(el, item);
  else if (type === "bubatz-cantina") updateBubatzCantina(el, item);
  else if (type === "fruppis-landadel") updateFruppisLandadel(el, item);
  else if (type === "animated-background") updateAnimatedBackground(el, item);
  else if (type === "shape.divider") updateDivider(el, item);
  else if (type === "shape.cam-ring") updateCamRing(el, item);
  else if (type === "shape.sticker") updateSticker(el, item);

  if (options?.seedDemo) {
    seedDemoContent(el, item, options);
  }
}

function seedDemoContent(el: HTMLElement, item: LayoutItem, options: PaintItemOptions): void {
  if (item.type === "chat" && options.demoChatMessages) {
    for (const msg of options.demoChatMessages) {
      appendChatMessage(el, msg);
    }
  }
  if (item.type === "alert" && options.demoAlert) {
    enqueueAlert(el, item, options.demoAlert);
  }
  if (item.type === "event-ticker" && options.demoTickerEvents) {
    for (const evt of options.demoTickerEvents) {
      pushEventTickerItem(el as never, item, {
        id: `demo-${evt.type}-${evt.text || "x"}`,
        type: evt.type,
        text: evt.text || ""
      });
    }
  }
}
