(function () {
  if (window.__ccsYtMusicBridge) {
    console.log("[CCS] YouTube Music Bridge läuft bereits.");
    return;
  }

  var port = Number("__CCS_BRIDGE_PORT__") || 43831;
  var baseUrl = "http://127.0.0.1:" + port + "/ytmusic";
  var pollMs = 900;
  var stateMs = 1200;
  var stopped = false;

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
      // YT Music progress is often in seconds.
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

  async function postState() {
    try {
      var state = readState();
      await fetch(baseUrl + "/state", {
        method: "POST",
        mode: "cors",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(state)
      });
    } catch (err) {
      // Bridge offline – still keep trying.
    }
  }

  async function pollCommands() {
    try {
      var res = await fetch(baseUrl + "/commands", { method: "GET", mode: "cors" });
      if (!res.ok) return;
      var data = await res.json();
      var commands = (data && data.commands) || [];
      for (var i = 0; i < commands.length; i++) {
        clickControl(String(commands[i] || "").toLowerCase());
      }
    } catch (err) {
      // ignore
    }
  }

  async function loop() {
    while (!stopped) {
      await postState();
      await pollCommands();
      await new Promise(function (r) { setTimeout(r, pollMs); });
    }
  }

  window.__ccsYtMusicBridge = {
    stop: function () { stopped = true; }
  };

  console.log("[CCS] YouTube Music Bridge gestartet → " + baseUrl);
  loop();
  setInterval(postState, stateMs);
})();
