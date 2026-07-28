import { prop } from '../../utils/prop';
import { escapeHtml } from '../../utils/html';

// Brand SVG paths (Simple Icons style, viewBox 0 0 24 24).
// customIconUrl overrides each slot.
export const SOCIALS_PLATFORMS = [
    {
      id: "twitch",
      label: "Twitch",
      showKey: "showTwitch",
      handleKey: "twitchHandle",
      urlKey: "twitchUrl",
      iconUrlKey: "twitchIconUrl",
      fa: "fa-brands fa-twitch",
      color: "#9146FF",
      urlFromHandle: (h) => "https://twitch.tv/" + encodeURIComponent(h),
      svg: "M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z"
    },
    {
      id: "youtube",
      label: "YouTube",
      showKey: "showYoutube",
      handleKey: "youtubeHandle",
      urlKey: "youtubeUrl",
      iconUrlKey: "youtubeIconUrl",
      fa: "fa-brands fa-youtube",
      color: "#FF0000",
      urlFromHandle: (h) => "https://youtube.com/@" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z"
    },
    {
      id: "discord",
      label: "Discord",
      showKey: "showDiscord",
      handleKey: "discordHandle",
      urlKey: "discordUrl",
      iconUrlKey: "discordIconUrl",
      fa: "fa-brands fa-discord",
      color: "#5865F2",
      urlFromHandle: (h) => (String(h).indexOf("http") === 0 ? h : "https://discord.gg/" + encodeURIComponent(h)),
      svg: "M20.317 4.3698a19.7913 19.7913 0 0 0-4.8851-1.5152.0741.0741 0 0 0-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 0 0-.0785-.037 19.7363 19.7363 0 0 0-4.8852 1.515.0699.0699 0 0 0-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 0 0 .0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 0 0 .0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 0 0-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 0 1-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 0 1 .0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 0 1 .0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 0 1-.0066.1276 12.2986 12.2986 0 0 1-1.873.8914.0766.0766 0 0 0-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 0 0 .0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 0 0 .0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 0 0-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z"
    },
    {
      id: "instagram",
      label: "Instagram",
      showKey: "showInstagram",
      handleKey: "instagramHandle",
      urlKey: "instagramUrl",
      iconUrlKey: "instagramIconUrl",
      fa: "fa-brands fa-instagram",
      color: "#E4405F",
      urlFromHandle: (h) => "https://instagram.com/" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M12 0C8.74 0 8.333.015 7.053.072 5.775.132 4.905.333 4.14.63c-.789.306-1.459.717-2.126 1.384S.935 3.35.63 4.14C.333 4.905.131 5.775.072 7.053.012 8.333 0 8.74 0 12s.015 3.667.072 4.947c.06 1.277.261 2.148.558 2.913.306.788.717 1.459 1.384 2.126.667.666 1.336 1.079 2.126 1.384.766.296 1.636.499 2.913.558C8.333 23.988 8.74 24 12 24s3.667-.015 4.947-.072c1.277-.06 2.148-.262 2.913-.558.788-.306 1.459-.718 2.126-1.384.666-.667 1.079-1.335 1.384-2.126.296-.765.499-1.636.558-2.913.06-1.28.072-1.687.072-4.947s-.015-3.667-.072-4.947c-.06-1.277-.262-2.149-.558-2.913-.306-.789-.718-1.459-1.384-2.126C21.319 1.347 20.651.935 19.86.63c-.765-.297-1.636-.499-2.913-.558C15.667.012 15.26 0 12 0zm0 2.16c3.203 0 3.585.016 4.85.071 1.17.055 1.805.249 2.227.415.562.217.96.477 1.382.896.419.42.679.819.896 1.381.164.422.36 1.057.413 2.227.055 1.265.07 1.647.07 4.85s-.015 3.585-.074 4.85c-.061 1.17-.256 1.805-.421 2.227-.224.562-.479.96-.899 1.382-.419.419-.824.679-1.38.896-.42.164-1.065.36-2.235.413-1.274.055-1.645.07-4.859.07-3.211 0-3.586-.015-4.859-.074-1.171-.061-1.816-.256-2.236-.421-.569-.224-.96-.479-1.379-.899-.421-.419-.69-.824-.9-1.38-.165-.42-.359-1.065-.42-2.235-.045-1.26-.061-1.649-.061-4.844 0-3.196.016-3.586.061-4.861.061-1.17.255-1.814.42-2.234.21-.57.479-.96.9-1.381.419-.419.81-.689 1.379-.898.42-.166 1.051-.361 2.221-.421 1.275-.045 1.65-.06 4.859-.06l.045.03zm0 3.678c-3.405 0-6.162 2.76-6.162 6.162 0 3.405 2.76 6.162 6.162 6.162 3.401 0 6.162-2.76 6.162-6.162 0-3.401-2.76-6.162-6.162-6.162zM12 16c-2.21 0-4-1.79-4-4s1.79-4 4-4 4 1.79 4 4-1.79 4-4 4zm7.846-10.405c0 .795-.646 1.44-1.44 1.44-.795 0-1.44-.645-1.44-1.44 0-.794.645-1.439 1.44-1.439.793-.001 1.44.645 1.44 1.439z"
    },
    {
      id: "tiktok",
      label: "TikTok",
      showKey: "showTiktok",
      handleKey: "tiktokHandle",
      urlKey: "tiktokUrl",
      iconUrlKey: "tiktokIconUrl",
      fa: "fa-brands fa-tiktok",
      color: "#69C9D0",
      urlFromHandle: (h) => "https://tiktok.com/@" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M12.525.02c1.31-.02 2.61-.01 3.91-.02.08 1.53.63 3.09 1.75 4.17 1.12 1.11 2.37.69 3.18 1.68v2.56a8.4 8.4 0 0 1-4.15-1.16v7.53a8.15 8.15 0 0 1-8.2 8.2 8.26 8.26 0 0 1-8.2-8.2c0-4.55 3.71-8.2 8.2-8.2.13 0 .26.02.39.03v2.66a5.35 5.35 0 0 0-.39-.03 5.52 5.52 0 0 0-5.52 5.52 5.52 5.52 0 0 0 5.52 5.52 5.54 5.54 0 0 0 5.52-5.52V.02h.01z"
    },
    {
      id: "x",
      label: "X",
      showKey: "showX",
      handleKey: "xHandle",
      urlKey: "xUrl",
      iconUrlKey: "xIconUrl",
      fa: "fa-brands fa-x-twitter",
      color: "#ffffff",
      urlFromHandle: (h) => "https://x.com/" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M18.901 1.153h3.68l-8.04 9.19L24 22.846h-7.406l-5.8-7.584-6.638 7.584H.474l8.6-9.83L0 1.154h7.594l5.243 6.932ZM17.61 20.644h2.039L6.486 3.24H4.298Z"
    },
    {
      id: "kick",
      label: "Kick",
      showKey: "showKick",
      handleKey: "kickHandle",
      urlKey: "kickUrl",
      iconUrlKey: "kickIconUrl",
      fa: "fa-brands fa-kickstarter-k",
      color: "#53FC18",
      urlFromHandle: (h) => "https://kick.com/" + encodeURIComponent(h),
      svg: "M14.563 0.443l-3.485 3.485 3.485 3.485v3.443l-6.928-6.928L14.563 0v0.443zm0 23.114l-3.485-3.485 3.485-3.485v-3.443l-6.928 6.928L14.563 24v-0.443zM9.437 12L1.5 4.063V0.62L12.817 11.937 1.5 23.38v-3.443L9.437 12z"
    },
    {
      id: "bluesky",
      label: "Bluesky",
      showKey: "showBluesky",
      handleKey: "blueskyHandle",
      urlKey: "blueskyUrl",
      iconUrlKey: "blueskyIconUrl",
      fa: "fa-brands fa-bluesky",
      color: "#1185FE",
      urlFromHandle: (h) => "https://bsky.app/profile/" + encodeURIComponent(h.replace(/^@/, "")),
      svg: "M12 10.8c-1.087-2.114-4.046-6.053-6.798-7.995C2.566.944 1.561 1.266.902 1.565.139 1.908 0 3.08 0 3.768c0 .69.378 5.65.624 6.479.815 2.736 3.713 3.66 6.383 3.364.136-.02.275-.039.415-.056-.138.022-.276.04-.415.056-3.912.58-7.389 2.004-2.83 7.078 5.013 5.19 6.87-1.113 7.823-4.308.953 3.195 2.05 9.271 7.733 4.308 4.267-4.308 1.172-6.498-2.74-7.078a8.741 8.741 0 0 1-.415-.056c.14.017.279.036.415.056 2.67.297 5.568-.628 6.383-3.364.246-.828.624-5.79.624-6.478 0-.69-.139-1.861-.902-2.206-.659-.298-1.664-.62-4.3 1.24C16.046 4.748 13.087 8.687 12 10.8Z"
    },
    {
      id: "custom1",
      label: "Custom 1",
      showKey: "showCustom1",
      handleKey: "custom1Handle",
      urlKey: "custom1Url",
      iconUrlKey: "custom1IconUrl",
      labelKey: "custom1Label",
      fa: "fa-solid fa-link",
      color: "#ff7a00",
      svg: "M10.59 13.41a1 1 0 0 1 0-1.41l4.24-4.24a1 1 0 1 1 1.41 1.41l-4.24 4.24a1 1 0 0 1-1.41 0zm-2.12 2.12a1 1 0 0 1 0-1.41l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a3 3 0 0 0 0 4.24l2.83 2.83a3 3 0 0 0 4.24 0l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a1 1 0 0 1-1.41 0l-2.83-2.83zm9.9-9.9a3 3 0 0 0-4.24 0l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a1 1 0 0 1 1.41 0l2.83 2.83a1 1 0 0 1 0 1.41l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a3 3 0 0 0 0-4.24l-2.83-2.83z"
    },
    {
      id: "custom2",
      label: "Custom 2",
      showKey: "showCustom2",
      handleKey: "custom2Handle",
      urlKey: "custom2Url",
      iconUrlKey: "custom2IconUrl",
      labelKey: "custom2Label",
      fa: "fa-solid fa-link",
      color: "#ffb36b",
      svg: "M10.59 13.41a1 1 0 0 1 0-1.41l4.24-4.24a1 1 0 1 1 1.41 1.41l-4.24 4.24a1 1 0 0 1-1.41 0zm-2.12 2.12a1 1 0 0 1 0-1.41l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a3 3 0 0 0 0 4.24l2.83 2.83a3 3 0 0 0 4.24 0l.71-.71a1 1 0 0 0-1.41-1.41l-.71.71a1 1 0 0 1-1.41 0l-2.83-2.83zm9.9-9.9a3 3 0 0 0-4.24 0l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a1 1 0 0 1 1.41 0l2.83 2.83a1 1 0 0 1 0 1.41l-.71.71a1 1 0 0 0 1.41 1.41l.71-.71a3 3 0 0 0 0-4.24l-2.83-2.83z"
    }
  ];

export const SOCIALS_VARIANTS = ["row", "pills", "cards", "stack", "neon", "minimal"];
  let socialsFaLoaded = false;

export function ensureSocialsFontAwesome() {
    if (socialsFaLoaded) return;
    if (document.getElementById("ccs-socials-fa")) {
      socialsFaLoaded = true;
      return;
    }
    const link = document.createElement("link");
    link.id = "ccs-socials-fa";
    link.rel = "stylesheet";
    link.href = "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.7.2/css/all.min.css";
    document.head.appendChild(link);
    socialsFaLoaded = true;
  }

export function socialsVariant(item) {
    const raw = String(prop(item, "variant", "row") || "row").toLowerCase();
    return SOCIALS_VARIANTS.includes(raw) ? raw : "row";
  }

export function socialsIconLibrary(item) {
    const raw = String(prop(item, "iconLibrary", "svg") || "svg").toLowerCase();
    return raw === "fontawesome" ? "fontawesome" : "svg";
  }

export function resolveSocialsEntries(item) {
    const platformId = String(prop(item, "platform", "") || "").trim().toLowerCase();
    if (platformId) {
      const platform = SOCIALS_PLATFORMS.find((p) => p.id === platformId) || SOCIALS_PLATFORMS[0];
      if (!platform) return [];
      const handle = String(
        prop(item, "handle", prop(item, platform.handleKey, "")) || ""
      ).trim();
      const urlRaw = String(
        prop(item, "url", prop(item, platform.urlKey, "")) || ""
      ).trim();
      const customIconUrl = String(
        prop(item, "iconUrl", prop(item, platform.iconUrlKey, "")) || ""
      ).trim();
      const labelDefault = platform.labelKey
        ? String(prop(item, platform.labelKey, platform.label) || platform.label)
        : platform.label;
      const label = String(prop(item, "label", labelDefault) || labelDefault).trim() || platform.label;
      let href = urlRaw;
      if (!href && handle && typeof platform.urlFromHandle === "function") {
        href = platform.urlFromHandle(handle);
      }
      return [{
        id: platform.id,
        label,
        handle,
        href,
        customIconUrl,
        fa: platform.fa,
        color: platform.color,
        svg: platform.svg
      }];
    }

    // Legacy: multi-platform props (showTwitch, …) for older layouts
    const entries = [];
    for (const platform of SOCIALS_PLATFORMS) {
      const enabledDefault = platform.id.indexOf("custom") === 0 ||
        platform.id === "kick" ||
        platform.id === "bluesky"
        ? false
        : true;
      if (prop(item, platform.showKey, enabledDefault) === false) continue;
      const handle = String(prop(item, platform.handleKey, "") || "").trim();
      const urlRaw = String(prop(item, platform.urlKey, "") || "").trim();
      const customIconUrl = String(prop(item, platform.iconUrlKey, "") || "").trim();
      const label = platform.labelKey
        ? String(prop(item, platform.labelKey, platform.label) || platform.label).trim() || platform.label
        : platform.label;
      let href = urlRaw;
      if (!href && handle && typeof platform.urlFromHandle === "function") {
        href = platform.urlFromHandle(handle);
      }
      entries.push({
        id: platform.id,
        label,
        handle,
        href,
        customIconUrl,
        fa: platform.fa,
        color: platform.color,
        svg: platform.svg
      });
    }
    return entries;
  }

export function renderSocialsIcon(entry, library) {
    if (entry.customIconUrl) {
      return `<img class="ccs-socials-icon ccs-socials-icon-img" src="${escapeHtml(entry.customIconUrl)}" alt="" />`;
    }
    if (library === "fontawesome" && entry.fa) {
      return `<i class="ccs-socials-icon ccs-socials-icon-fa ${escapeHtml(entry.fa)}" aria-hidden="true"></i>`;
    }
    return (
      `<svg class="ccs-socials-icon ccs-socials-icon-svg" viewBox="0 0 24 24" aria-hidden="true">` +
      `<path fill="currentColor" d="${entry.svg || ""}"></path></svg>`
    );
  }

export function createSocialsEl(item) {
    const el = document.createElement("div");
    el.className = "ccs-socials";
    el.innerHTML = `<div class="ccs-socials-list"></div>`;
    if (typeof ResizeObserver !== "undefined") {
      el._ro = new ResizeObserver(() => fitSocials(el));
      el._ro.observe(el);
    }
    updateSocials(el, item);
    return el;
  }

export function applySocialsVariant(el, item) {
    const variant = socialsVariant(item);
    SOCIALS_VARIANTS.forEach((name) => {
      el.classList.remove("ccs-socials-v-" + name);
    });
    el.classList.add("ccs-socials-v-" + variant);
    el.dataset.variant = variant;
    const colorMode = String(prop(item, "colorMode", "brand") || "brand").toLowerCase();
    el.classList.toggle("ccs-socials-color-brand", colorMode !== "mono");
    el.classList.toggle("ccs-socials-color-mono", colorMode === "mono");
    el.classList.toggle("hide-labels", prop(item, "showLabels", true) === false);
    el.classList.toggle("hide-handles", prop(item, "showHandles", true) === false);
  }

export function fitSocials(el) {
    if (!el) return;
    // Reference matches WIDGET_DEFAULTS.socials (280×72)
    const refW = 280;
    const refH = 72;
    const w = Math.max(1, el.clientWidth || el.offsetWidth || refW);
    const h = Math.max(1, el.clientHeight || el.offsetHeight || refH);
    const scale = Math.max(0.5, Math.min(1.4, Math.min(w / refW, h / refH)));
    el.style.setProperty("--ccs-socials-scale", String(scale));
  }

export function updateSocials(el, item) {
    if (!el) return;
    const library = socialsIconLibrary(item);
    if (library === "fontawesome") {
      ensureSocialsFontAwesome();
    }
    applySocialsVariant(el, item);
    const iconSize = Number(prop(item, "iconSize", 36)) || 36;
    const gap = Number(prop(item, "gap", 12)) || 12;
    const iconColor = String(prop(item, "iconColor", "#ffffff") || "#ffffff");
    el.style.setProperty("--ccs-socials-icon-size", iconSize + "px");
    el.style.setProperty("--ccs-socials-gap", gap + "px");
    el.style.setProperty("--ccs-socials-icon-color", iconColor);

    const list = el.querySelector(".ccs-socials-list");
    if (!list) return;
    const entries = resolveSocialsEntries(item);
    list.innerHTML = entries.map((entry) => {
      const handleText = entry.handle
        ? (entry.handle.indexOf("@") === 0 || entry.id === "discord" ? entry.handle : "@" + entry.handle)
        : "";
      const style = entry.color ? `--ccs-socials-brand:${entry.color}` : "";
      const hrefAttr = entry.href ? ` data-href="${escapeHtml(entry.href)}"` : "";
      return (
        `<div class="ccs-socials-item" data-id="${escapeHtml(entry.id)}" style="${style}"${hrefAttr}>` +
        `<div class="ccs-socials-glyph">${renderSocialsIcon(entry, library)}</div>` +
        `<div class="ccs-socials-meta">` +
        `<div class="ccs-socials-label">${escapeHtml(entry.label)}</div>` +
        `<div class="ccs-socials-handle">${escapeHtml(handleText)}</div>` +
        `</div></div>`
      );
    }).join("");
    fitSocials(el);
    requestAnimationFrame(() => fitSocials(el));
  }
