(function () {
  var port = Number("__CCS_BRIDGE_PORT__") || 43831;
  var baseUrl = "http://127.0.0.1:" + port + "/ytmusic";
  var pollMs = 900;
  var minBackoffMs = 1000;
  var maxBackoffMs = 15000;
  var healthEveryMs = 5000;

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

  var lastCoverUrl = "";
  var lastAlbum = "";

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

  function queryAll(selectors) {
    var out = [];
    for (var i = 0; i < selectors.length; i++) {
      var list = document.querySelectorAll(selectors[i]);
      for (var j = 0; j < list.length; j++) out.push(list[j]);
    }
    return out;
  }

  function splitByline(raw) {
    var text = String(raw || "").replace(/\s+/g, " ").trim();
    if (!text) return { artist: "", album: "" };
    var parts = text.split(/\s*[•·‧∙|]\s*/).map(function (p) { return p.trim(); }).filter(Boolean);
    if (parts.length >= 2) {
      return { artist: parts[0], album: parts.slice(1).join(" • ") };
    }
    return { artist: text, album: "" };
  }

  function upgradeCoverUrl(url) {
    if (!url) return "";
    var u = String(url).trim();
    if (!u || u.indexOf("data:") === 0) return "";
    // Googleusercontent / YT Music thumbs: Größe hochskalieren.
    u = u.replace(/=w\d+-h\d+([^?]*)/i, "=w544-h544$1");
    u = u.replace(/=s\d+([^?]*)/i, "=s544$1");
    // Video-Thumbnails: bessere Auflösung bevorzugen.
    u = u.replace(/\/(default|mqdefault|hqdefault|sddefault)\.jpg/i, "/hq720.jpg");
    return u;
  }

  function isUsableCoverUrl(url) {
    if (!url) return false;
    var u = String(url).toLowerCase();
    if (u.indexOf("data:") === 0) return false;
    if (u.indexOf("avatar") >= 0 && u.indexOf("lh3.googleusercontent") < 0) return false;
    return u.indexOf("http") === 0 || u.indexOf("//") === 0;
  }

  function readMediaSession() {
    try {
      var md = navigator.mediaSession && navigator.mediaSession.metadata;
      if (!md) return null;
      var artwork = [];
      try { artwork = md.artwork ? Array.from(md.artwork) : []; } catch (e) { artwork = []; }
      var best = "";
      var bestArea = -1;
      for (var i = 0; i < artwork.length; i++) {
        var item = artwork[i];
        if (!item || !item.src) continue;
        var area = 0;
        var sizes = String(item.sizes || "");
        var m = sizes.match(/(\d+)\s*[x×]\s*(\d+)/i);
        if (m) area = Number(m[1]) * Number(m[2]);
        else area = i;
        if (area >= bestArea) {
          bestArea = area;
          best = item.src;
        }
      }
      return {
        title: md.title || "",
        artist: md.artist || "",
        album: md.album || "",
        coverUrl: best
      };
    } catch (e) {
      return null;
    }
  }

  function readCoverFromDom() {
    var imgs = queryAll([
      "ytmusic-player-bar #song-image img",
      "ytmusic-player-bar yt-img-shadow img",
      "ytmusic-player-bar img#img",
      "ytmusic-player-bar .image img",
      "ytmusic-player #song-image img",
      "ytmusic-player yt-img-shadow#song-image img",
      "ytmusic-player img#img",
      "#song-image img",
      "ytmusic-player-page #song-image img"
    ]);

    for (var i = 0; i < imgs.length; i++) {
      var img = imgs[i];
      var src = img.currentSrc || img.src || img.getAttribute("src") || img.getAttribute("data-src") || "";
      if (!isUsableCoverUrl(src)) continue;
      // Winzige Tracking-/Placeholder-Images überspringen.
      var w = Number(img.naturalWidth || img.width || 0);
      var h = Number(img.naturalHeight || img.height || 0);
      if ((w > 0 && w < 20) || (h > 0 && h < 20)) continue;
      return src;
    }

    var videos = queryAll([
      "ytmusic-player video",
      "#song-video video",
      "ytmusic-player #song-video video",
      "video.video-stream",
      "ytmusic-player-page video"
    ]);
    for (var v = 0; v < videos.length; v++) {
      var video = videos[v];
      var poster = video.poster || video.getAttribute("poster") || "";
      if (isUsableCoverUrl(poster)) return poster;

      // Manche Builds legen die Video-ID nur in der Seite ab – Fallback auf sichtbares Thumbnail neben dem Video.
      var thumb = video.closest && video.closest("ytmusic-player, #song-video, ytmusic-player-page");
      if (thumb) {
        var tImg = thumb.querySelector("img#img, yt-img-shadow img, img");
        if (tImg) {
          var tSrc = tImg.currentSrc || tImg.src || tImg.getAttribute("src") || "";
          if (isUsableCoverUrl(tSrc)) return tSrc;
        }
      }
    }

    return "";
  }

  function readAlbumFromDom(bylineAlbum) {
    if (bylineAlbum) return bylineAlbum;

    var albumLink = queryFirst([
      "ytmusic-player-bar .byline a[href*='browse/MPREb']",
      "ytmusic-player-bar .byline a[href*='browse/FEmusic_library_privately_owned_release']",
      "ytmusic-player-bar .subtitle a[href*='browse/MPREb']",
      "ytmusic-player-page .byline a[href*='browse/MPREb']",
      "ytmusic-player-bar .byline a:nth-of-type(2)",
      "#content-info .subtitle a:nth-of-type(2)"
    ]);
    var fromLink = textOf(albumLink);
    if (fromLink) return fromLink;

    var albumEl = queryFirst([
      "ytmusic-player-bar .album-name",
      "ytmusic-player-page .album-title",
      "ytmusic-player-page .song-album"
    ]);
    return textOf(albumEl);
  }

  function readState() {
    var media = readMediaSession();

    var titleEl = queryFirst([
      ".title.ytmusic-player-bar",
      "ytmusic-player-bar .title",
      "#content-info .title",
      "ytmusic-player-bar yt-formatted-string.title"
    ]);
    var artistEl = queryFirst([
      ".byline.ytmusic-player-bar",
      "ytmusic-player-bar .byline",
      "#content-info .subtitle",
      "ytmusic-player-bar yt-formatted-string.byline"
    ]);

    var byline = splitByline(textOf(artistEl));
    var title = textOf(titleEl) || (media && media.title) || "";
    var artist = byline.artist || (media && media.artist) || "";
    var album = readAlbumFromDom(byline.album) || (media && media.album) || "";

    var coverUrl = upgradeCoverUrl(readCoverFromDom()) ||
      upgradeCoverUrl(media && media.coverUrl) ||
      "";

    // Kurzzeitige DOM-Lücken (z. B. Video-Wechsel) mit letztem guten Wert überbrücken.
    if (coverUrl) lastCoverUrl = coverUrl;
    else coverUrl = lastCoverUrl;

    if (album) lastAlbum = album;
    else if (!album && lastAlbum && title) album = lastAlbum;

    var playPause = queryFirst([
      "#play-pause-button",
      "ytmusic-player-bar #play-pause-button",
      "tp-yt-paper-icon-button#play-pause-button"
    ]);
    var isPlaying = false;
    if (playPause) {
      var label = (playPause.getAttribute("title") || playPause.getAttribute("aria-label") || "").toLowerCase();
      isPlaying = label.indexOf("pause") >= 0 || label.indexOf("pausieren") >= 0;
    } else if (navigator.mediaSession && navigator.mediaSession.playbackState) {
      isPlaying = navigator.mediaSession.playbackState === "playing";
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
      album: album,
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
      await postState();
    }
    if (!stopped) {
      loopTimer = setTimeout(tick, connected ? pollMs : nextDelayMs);
    }
  }

  function onVisibilityOrOnline() {
    if (stopped) return;
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

  heartbeatTimer = setInterval(function () {
    if (!stopped) postState();
  }, 2500);

  log("YouTube Music Bridge gestartet → " + baseUrl + " (Auto-Reconnect aktiv)");
  tick();
})();
