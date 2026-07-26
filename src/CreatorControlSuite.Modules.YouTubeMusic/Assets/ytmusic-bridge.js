(function () {
  var port = Number("__CCS_BRIDGE_PORT__") || 43831;
  var baseUrl = "http://127.0.0.1:" + port + "/ytmusic";
  var pollMs = 900;
  var minBackoffMs = 1000;
  var maxBackoffMs = 15000;
  var healthEveryMs = 5000;

  // Bereits laufende Instanz sauber neu starten (Reconnect / Re-Inject).
  if (window.__ccsYtMusicBridge && typeof window.__ccsYtMusicBridge.stop === "function") {
    try { window.__ccsYtMusicBridge.stop(); } catch (e) { /* ignore */ }
  }

  var stopped = false;
  var connected = false;
  var failCount = 0;
  var nextDelayMs = pollMs;
  var lastHealthAt = 0;
  var loopTimer = null;
  var heartbeatTimer = null;

  function log(msg) {
    try { console.log("[CCS] " + msg); } catch (e) { /* ignore */ }
  }

  function textOf(el) {
    return el && el.textContent ? el.textContent.trim() : "";
  }

  function queryFirst(selectors) {
    for (var i = 0; i < selectors.length; i++) {
      var el = document.querySelector(selectors[i]);
      if (el) return el;
    }
    return null;
  }

  function readState() {
    var titleEl = queryFirst([
      ".title.ytmusic-player-bar",
      "ytmusic-player-bar .title",
      "#content-info .title"
    ]);
    var artistEl = queryFirst([
      ".byline.ytmusic-player-bar",
      "ytmusic-player-bar .byline",
      "#content-info .subtitle"
    ]);
    var coverEl = queryFirst([
      "ytmusic-player-bar img#img",
      "ytmusic-player-bar .image img",
      "#song-image img"
    ]);
    var playPause = queryFirst([
      "#play-pause-button",
      "ytmusic-player-bar #play-pause-button",
      "tp-yt-paper-icon-button#play-pause-button"
    ]);

    var title = textOf(titleEl);
    var artist = textOf(artistEl);
    var coverUrl = coverEl && (coverEl.src || coverEl.getAttribute("src")) || "";
    var isPlaying = false;
    if (playPause) {
      var label = (playPause.getAttribute("title") || playPause.getAttribute("aria-label") || "").toLowerCase();
      isPlaying = label.indexOf("pause") >= 0 || label.indexOf("pausieren") >= 0;
    }

    var progressMs = 0;
    var durationMs = 0;
    var progressEl = document.querySelector("ytmusic-player-bar #progress-bar");
    if (progressEl) {
      var value = Number(progressEl.getAttribute("value") || progressEl.value || 0);
      var max = Number(progressEl.getAttribute("aria-valuemax") || progressEl.max || 0);
      if (max > 0 && max < 86400) {
        progressMs = Math.round(value * 1000);
        durationMs = Math.round(max * 1000);
      } else {
        progressMs = Math.round(value);
        durationMs = Math.round(max);
      }
    }

    return {
      title: title,
      artist: artist,
      album: "",
      coverUrl: coverUrl,
      isPlaying: isPlaying,
      progressMs: progressMs,
      durationMs: durationMs
    };
  }

  function clickControl(kind) {
    var map = {
      play: ["#play-pause-button"],
      pause: ["#play-pause-button"],
      playpause: ["#play-pause-button"],
      next: [".next-button", "ytmusic-player-bar .next-button", "#navigate-with-fingerprints-button"],
      previous: [".previous-button", "ytmusic-player-bar .previous-button"]
    };
    var selectors = map[kind] || [];
    var el = queryFirst(selectors);
    if (el) el.click();
  }

  function markConnected() {
    if (!connected) {
      connected = true;
      log("Bridge wieder verbunden → " + baseUrl);
    }
    failCount = 0;
    nextDelayMs = pollMs;
  }

  function markDisconnected(reason) {
    failCount += 1;
    nextDelayMs = Math.min(maxBackoffMs, Math.max(minBackoffMs, pollMs * Math.pow(1.6, Math.min(failCount, 8))));
    if (connected || failCount === 1 || failCount % 5 === 0) {
      log("Bridge offline (" + (reason || "netzwerk") + ") – Reconnect in " + Math.round(nextDelayMs) + " ms");
    }
    connected = false;
  }

  async function checkHealth() {
    var now = Date.now();
    if (now - lastHealthAt < healthEveryMs) return connected;
    lastHealthAt = now;
    try {
      var res = await fetch(baseUrl + "/health", {
        method: "GET",
        mode: "cors",
        cache: "no-store"
      });
      if (!res.ok) {
        markDisconnected("health " + res.status);
        return false;
      }
      markConnected();
      return true;
    } catch (err) {
      markDisconnected("health");
      return false;
    }
  }

  async function postState() {
    try {
      var state = readState();
      var res = await fetch(baseUrl + "/state", {
        method: "POST",
        mode: "cors",
        cache: "no-store",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(state)
      });
      if (!res.ok) {
        markDisconnected("state " + res.status);
        return false;
      }
      markConnected();
      return true;
    } catch (err) {
      markDisconnected("state");
      return false;
    }
  }

  async function pollCommands() {
    if (!connected) return;
    try {
      var res = await fetch(baseUrl + "/commands", {
        method: "GET",
        mode: "cors",
        cache: "no-store"
      });
      if (!res.ok) {
        markDisconnected("commands " + res.status);
        return;
      }
      var data = await res.json();
      var commands = (data && data.commands) || [];
      for (var i = 0; i < commands.length; i++) {
        clickControl(String(commands[i] || "").toLowerCase());
      }
      markConnected();
    } catch (err) {
      markDisconnected("commands");
    }
  }

  async function tick() {
    if (stopped) return;
    var ok = await checkHealth();
    if (ok) {
      await postState();
      await pollCommands();
    } else {
      // Beim Reconnect zusätzlich State pushen, sobald Health wieder ok wäre –
      // checkHealth hat bereits versucht; ein direkter State-Versuch hilft bei Race.
      await postState();
    }
    if (!stopped) {
      loopTimer = setTimeout(tick, connected ? pollMs : nextDelayMs);
    }
  }

  function onVisibilityOrOnline() {
    if (stopped) return;
    // Sofort versuchen, statt auf Backoff zu warten.
    failCount = 0;
    nextDelayMs = pollMs;
    if (loopTimer) {
      clearTimeout(loopTimer);
      loopTimer = null;
    }
    tick();
  }

  function stop() {
    stopped = true;
    if (loopTimer) clearTimeout(loopTimer);
    if (heartbeatTimer) clearInterval(heartbeatTimer);
    loopTimer = null;
    heartbeatTimer = null;
    try {
      document.removeEventListener("visibilitychange", onVisibilityOrOnline);
      window.removeEventListener("online", onVisibilityOrOnline);
      window.removeEventListener("focus", onVisibilityOrOnline);
    } catch (e) { /* ignore */ }
    connected = false;
    log("Bridge gestoppt.");
  }

  window.__ccsYtMusicBridge = {
    stop: stop,
    reconnect: function () {
      if (stopped) return;
      onVisibilityOrOnline();
    },
    isConnected: function () { return connected; },
    baseUrl: baseUrl
  };

  document.addEventListener("visibilitychange", onVisibilityOrOnline);
  window.addEventListener("online", onVisibilityOrOnline);
  window.addEventListener("focus", onVisibilityOrOnline);

  // Zusätzlicher Heartbeat hält die Verbindung auch bei SPA-Navigation warm.
  heartbeatTimer = setInterval(function () {
    if (!stopped) postState();
  }, 2500);

  log("YouTube Music Bridge gestartet → " + baseUrl + " (Auto-Reconnect aktiv)");
  tick();
})();
