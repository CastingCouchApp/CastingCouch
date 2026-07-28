/** Konstanten & State für die Overlay-Webserver-Simulation. */

export const WIDGET_TYPES = [
  "online",
  "alert",
  "music",
  "chat",
  "ending-stats",
  "text",
  "image",
  "countdown",
  "socials",
  "partner-roulette",
  "goal-bar",
  "event-ticker",
  "viewer-count",
  "lower-third",
  "qr-code",
  "brb-panel",
  "announcement-bar",
  "animated-background"
];

export const SHAPE_TYPES = [
  "frame",
  "frame.card",
  "shape.vignette",
  "shape.scene-bg",
  "shape.cutout",
  "shape.divider",
  "shape.cam-ring",
  "shape.sticker"
];

export const SIZE_PRESETS = [
  { id: "1080p", label: "1920 × 1080 (Full HD)", width: 1920, height: 1080 },
  { id: "720p", label: "1280 × 720 (HD)", width: 1280, height: 720 },
  { id: "1440p", label: "2560 × 1440 (QHD)", width: 2560, height: 1440 },
  { id: "4k", label: "3840 × 2160 (4K)", width: 3840, height: 2160 },
  { id: "1080p-vert", label: "1080 × 1920 (Vertical)", width: 1080, height: 1920 },
  { id: "720p-vert", label: "720 × 1280 (Vertical)", width: 720, height: 1280 },
  { id: "square", label: "1080 × 1080 (Square)", width: 1080, height: 1080 }
];

export function defaultLayout(name = "Dev Canvas") {
  return {
    version: 1,
    name,
    canvasWidth: 1920,
    canvasHeight: 1080,
    items: []
  };
}

export function defaultCanvases() {
  return {
    selectedId: "default",
    canvases: [
      { id: "default", name: "Dev Canvas" },
      { id: "just-chatting", name: "Just Chatting" }
    ]
  };
}

export function createOverlayData() {
  const now = Date.now();
  return {
    stream: {
      isLive: true,
      phase: "Live",
      startedAt: new Date(now - 42 * 60 * 1000).toISOString(),
      endedAt: null,
      elapsedSeconds: 42 * 60,
      viewerCount: 128,
      currentScene: "Just Chatting"
    },
    twitch: {
      channelName: "devstreamer",
      title: "Overlay Editor Dev",
      category: "Just Chatting",
      followers: 1542,
      followerGoal: 2000,
      lastFollower: "alice",
      lastEvent: "follow",
      followerGoalState: { title: "Follower", current: 1542, target: 2000 },
      subGoalState: { title: "Subs", current: 12, target: 50 },
      donationGoalState: { title: "Bits", current: 340, target: 1000 }
    },
    spotify: {
      provider: "spotify",
      providerDisplayName: "Spotify",
      connected: true,
      isPlaying: true,
      title: "Night Drive",
      artist: "Synthwave Dev",
      album: "Hot Reload",
      coverUrl: "",
      cover: "",
      showInOverlay: true,
      progressMs: 42000,
      durationMs: 210000,
      statusText: "Playing",
      showTitle: true,
      showArtist: true,
      showAlbumCover: true,
      showProgress: true,
      hideWhenPaused: false,
      hideWhenMuted: false
    },
    music: {
      provider: "spotify",
      providerDisplayName: "Spotify",
      connected: true,
      isPlaying: true,
      title: "Night Drive",
      artist: "Synthwave Dev",
      album: "Hot Reload",
      coverUrl: "",
      progressMs: 42000,
      durationMs: 210000
    },
    obs: {
      connected: true,
      currentScene: "Just Chatting",
      microphoneMuted: false,
      desktopAudioMuted: false
    },
    alerts: {
      isRunning: false,
      currentType: "",
      queueLength: 0
    },
    stats: {
      followersGained: 17,
      peakViewers: 256,
      averageViewers: 110,
      streamTimeSeconds: 42 * 60,
      chatMessages: 88,
      alertsPlayed: 5,
      newSubscriptions: 3,
      giftSubscriptions: 2,
      bitsCheered: 340,
      incomingRaids: 1
    },
    branding: {
      displayName: "Dev Streamer",
      channelName: "devstreamer",
      accentColor: "#FF8C00",
      logoPath: ""
    },
    countdown: {
      isRunning: true,
      remainingSeconds: 300,
      totalSeconds: 600,
      endsAt: new Date(now + 300_000).toISOString(),
      label: "BRB",
      mode: "manual"
    },
    updatedAt: new Date(now).toISOString()
  };
}

export function chatConfig() {
  return {
    enabled: true,
    showTwitchEvents: true,
    enableBttv: false,
    enableFfz: false,
    enableSevenTv: false,
    backgroundType: "None",
    backgroundColor: "#000000",
    backgroundOpacity: 0,
    paddingPx: 12,
    borderRadiusPx: 8,
    gapPx: 6,
    fontSizePx: 18,
    fontFamily: "Segoe UI, sans-serif",
    backgroundVersion: "0"
  };
}

function evt(source, type, summary, data = {}) {
  return {
    source,
    type,
    at: new Date().toISOString(),
    summary,
    data
  };
}

const CHAT_USERS = [
  { userName: "alice", color: "#FF7A59" },
  { userName: "bob", color: "#59A8FF" },
  { userName: "carol", color: "#7DFF59" },
  { userName: "dave", color: "#FF59D0" }
];

const CHAT_LINES = [
  "Hot reload wirkt schon!",
  "Nice Overlay 🔥",
  "Kannst du Goal Bar größer machen?",
  "LGTM",
  "BRB in 5",
  "First time here, hi!"
];

const TRACKS = [
  { title: "Night Drive", artist: "Synthwave Dev" },
  { title: "Pixel Rain", artist: "Lo-Fi Bench" },
  { title: "Commit Message", artist: "The Refactors" },
  { title: "Green Build", artist: "CI Collective" }
];

const ALERTS = [
  { alertType: "follow", user: "newbie42", summary: "newbie42 folgt jetzt" },
  { alertType: "subscribe", user: "subqueen", summary: "subqueen hat subscribed" },
  { alertType: "cheer", user: "bitlord", summary: "bitlord cheer 100" },
  { alertType: "raid", user: "raidboss", summary: "raidboss raidet mit 25" }
];

/**
 * @param {ReturnType<typeof createOverlayData>} data
 * @param {(e: object) => void} publish
 * @param {{ persist?: () => void }} [opts]
 */
export function createSimulator(data, publish, opts = {}) {
  let step = 0;
  let chatSeq = 0;

  function persist() {
    opts.persist?.();
  }

  function tickCountdown() {
    const cd = data.countdown;
    if (!cd || !cd.isRunning) return;
    cd.remainingSeconds = Math.max(0, Number(cd.remainingSeconds) - 1);
    if (cd.remainingSeconds <= 0) {
      cd.isRunning = false;
      cd.endsAt = null;
    } else {
      cd.endsAt = new Date(Date.now() + cd.remainingSeconds * 1000).toISOString();
    }
    data.updatedAt = new Date().toISOString();
    persist();
    publish(
      evt("app", "app.countdown", `Countdown: ${cd.remainingSeconds}s`, {
        isRunning: String(cd.isRunning),
        remainingSeconds: String(cd.remainingSeconds),
        totalSeconds: String(cd.totalSeconds || 0),
        label: cd.label || "Countdown",
        endsAt: cd.endsAt || ""
      })
    );
  }

  function tickChat() {
    const user = CHAT_USERS[chatSeq % CHAT_USERS.length];
    const text = CHAT_LINES[chatSeq % CHAT_LINES.length];
    chatSeq += 1;
    const messageId = `dev-msg-${chatSeq}`;
    data.stats.chatMessages = Number(data.stats.chatMessages || 0) + 1;
    data.updatedAt = new Date().toISOString();
    persist();
    publish(
      evt("twitch", "channel.chat.message", `${user.userName}: ${text}`, {
        messageId,
        userName: user.userName,
        userLogin: user.userName.toLowerCase(),
        color: user.color,
        badges: "[]",
        parts: JSON.stringify([{ type: "text", text }])
      })
    );
  }

  function tickAlert() {
    const a = ALERTS[step % ALERTS.length];
    publish(
      evt("app", "app.alert", a.summary, {
        alertType: a.alertType,
        user: a.user
      })
    );
    if (a.alertType === "follow") {
      data.twitch.followers = Number(data.twitch.followers || 0) + 1;
      data.stats.followersGained = Number(data.stats.followersGained || 0) + 1;
      data.twitch.lastFollower = a.user;
      data.twitch.lastEvent = "follow";
    } else if (a.alertType === "subscribe") {
      data.stats.newSubscriptions = Number(data.stats.newSubscriptions || 0) + 1;
      data.twitch.lastEvent = "subscribe";
    } else if (a.alertType === "cheer") {
      data.stats.bitsCheered = Number(data.stats.bitsCheered || 0) + 100;
      data.twitch.lastEvent = "cheer";
    } else if (a.alertType === "raid") {
      data.stats.incomingRaids = Number(data.stats.incomingRaids || 0) + 1;
      data.twitch.lastEvent = "raid";
    }
    data.stats.alertsPlayed = Number(data.stats.alertsPlayed || 0) + 1;
    data.updatedAt = new Date().toISOString();
    persist();
  }

  function tickMusic() {
    const track = TRACKS[step % TRACKS.length];
    for (const key of ["music", "spotify"]) {
      const m = data[key];
      if (!m) continue;
      m.title = track.title;
      m.artist = track.artist;
      m.isPlaying = true;
      m.connected = true;
      m.progressMs = 5000 + (step * 11000) % 180000;
    }
    data.updatedAt = new Date().toISOString();
    persist();
    publish(
      evt("app", "app.music.track", `${track.artist} – ${track.title}`, {
        provider: "spotify",
        providerDisplayName: "Spotify",
        title: track.title,
        artist: track.artist,
        coverUrl: ""
      })
    );
  }

  function tickViewers() {
    const delta = (step % 2 === 0 ? 1 : -1) * (3 + (step % 5));
    data.stream.viewerCount = Math.max(12, Number(data.stream.viewerCount || 0) + delta);
    data.stats.peakViewers = Math.max(
      Number(data.stats.peakViewers || 0),
      Number(data.stream.viewerCount)
    );
    data.stream.elapsedSeconds = Number(data.stream.elapsedSeconds || 0) + 4;
    data.stats.streamTimeSeconds = data.stream.elapsedSeconds;
    data.updatedAt = new Date().toISOString();
    persist();
    publish(
      evt("app", "app.stream.live", "Stream live", {
        isLive: "true"
      })
    );
  }

  function tickScene() {
    const scenes = ["Just Chatting", "Gaming", "BRB", "Starting Soon"];
    const scene = scenes[step % scenes.length];
    data.stream.currentScene = scene;
    data.obs.currentScene = scene;
    data.updatedAt = new Date().toISOString();
    persist();
    publish(
      evt("app", "app.obs.scene", `Szene: ${scene}`, {
        scene
      })
    );
  }

  const actions = [tickChat, tickAlert, tickMusic, tickViewers, tickChat, tickScene];

  return {
    start(intervalMs = 4000) {
      const countdownTimer = setInterval(tickCountdown, 1000);
      const eventTimer = setInterval(() => {
        const action = actions[step % actions.length];
        step += 1;
        try {
          action();
        } catch (err) {
          console.error("[dev-sim]", err);
        }
      }, intervalMs);
      return () => {
        clearInterval(countdownTimer);
        clearInterval(eventTimer);
      };
    }
  };
}

export { evt as makeEvent };
