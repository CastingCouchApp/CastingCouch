#!/usr/bin/env node
/**
 * Browser-Dev: Overlay-Webserver-Simulation + esbuild Watch + Live-Reload.
 *
 * Entspricht dem lokalen Overlay-Server der App (Port default 8765):
 * /editor /view /w /chat /layout /data /ws /health /extensions /obs …
 *
 * Start: npm run dev
 * Port:  CCS_DEV_PORT (default 8765)
 * Host:  CCS_DEV_HOST (default 127.0.0.1)
 * Sim:   CCS_DEV_SIM=0 zum Abschalten der Event-Simulation
 */
import * as esbuild from "esbuild";
import { createServer, STATUS_CODES } from "node:http";
import {
  existsSync,
  mkdirSync,
  renameSync,
  unlinkSync
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createSimulator } from "./mock-state.mjs";
import { createOverlayServer } from "./overlay-server.mjs";
import { upgradeWebSocket } from "./ws.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(__dirname, "..");
const MODULE_ROOT = resolve(ROOT, "..");
const PORT = Number(process.env.CCS_DEV_PORT || 8765) || 8765;
const HOST = process.env.CCS_DEV_HOST || "127.0.0.1";
const SIM_ENABLED = process.env.CCS_DEV_SIM !== "0";

const LIVE_RELOAD_SNIPPET = `
<script>
(function () {
  var proto = location.protocol === "https:" ? "wss:" : "ws:";
  var url = proto + "//" + location.host + "/__dev_reload";
  function connect() {
    var ws = new WebSocket(url);
    ws.onmessage = function (ev) {
      if (ev.data === "reload") location.reload();
    };
    ws.onclose = function () { setTimeout(connect, 800); };
  }
  connect();
})();
</script>
`;

/** @type {Set<{send:(t:string)=>void}>} */
const reloadClients = new Set();
let reloadTimer = null;

function injectReload(html) {
  if (html.includes("/__dev_reload")) return html;
  if (html.includes("</body>")) {
    return html.replace("</body>", LIVE_RELOAD_SNIPPET + "</body>");
  }
  return html + LIVE_RELOAD_SNIPPET;
}

function broadcastReload() {
  if (reloadTimer) clearTimeout(reloadTimer);
  reloadTimer = setTimeout(() => {
    reloadTimer = null;
    for (const c of reloadClients) {
      try {
        c.send("reload");
      } catch {
        /* ignore */
      }
    }
  }, 120);
}

function finalizeCss(entry) {
  if (!entry.outCss) return;
  const autoCss = entry.outJs.replace(/\.js$/i, ".css");
  if (!existsSync(autoCss)) return;
  if (autoCss.toLowerCase() === entry.outCss.toLowerCase()) return;
  if (existsSync(entry.outCss)) {
    try {
      unlinkSync(entry.outCss);
    } catch {
      /* ignore */
    }
  }
  renameSync(autoCss, entry.outCss);
}

const entries = [
  {
    in: join(ROOT, "src/shared/index.ts"),
    outJs: join(ROOT, "shared/runtime.js"),
    outCss: join(ROOT, "shared/styles.css")
  },
  {
    in: join(ROOT, "src/editor/main.ts"),
    outJs: join(ROOT, "editor/editor.js"),
    outCss: join(ROOT, "editor/editor.css")
  },
  {
    in: join(ROOT, "src/view/main.ts"),
    outJs: join(ROOT, "view/view.js"),
    outCss: null
  },
  {
    in: join(ROOT, "src/solo/main.ts"),
    outJs: join(ROOT, "solo/solo.js"),
    outCss: null
  }
];

async function startBundler() {
  for (const entry of entries) {
    mkdirSync(dirname(entry.outJs), { recursive: true });
    if (entry.outCss) mkdirSync(dirname(entry.outCss), { recursive: true });

    const ctx = await esbuild.context({
      entryPoints: [entry.in],
      outfile: entry.outJs,
      bundle: true,
      format: "iife",
      platform: "browser",
      target: ["es2020"],
      minify: false,
      keepNames: true,
      sourcemap: true,
      logLevel: "warning",
      loader: {
        ".css": "css",
        ".png": "dataurl",
        ".svg": "dataurl"
      },
      plugins: [
        {
          name: "ccs-dev-reload",
          setup(build) {
            build.onEnd((result) => {
              if (result.errors && result.errors.length) return;
              finalizeCss(entry);
              broadcastReload();
            });
          }
        }
      ]
    });
    await ctx.watch();
  }
}

function text(res, status, body) {
  res.writeHead(status, {
    "Content-Type": "text/plain; charset=utf-8",
    "Content-Length": Buffer.byteLength(body)
  });
  res.end(body);
}

async function main() {
  const overlay = createOverlayServer({
    host: HOST,
    port: PORT,
    canvasRoot: ROOT,
    chatRoot: join(MODULE_ROOT, "ChatOverlay"),
    dataDir: join(__dirname, ".data"),
    layoutDir: join(__dirname, ".layouts"),
    extensionsDir: join(__dirname, "extensions"),
    injectHtml: injectReload
  });

  console.log("Building Canvas Overlay (watch)…");
  await startBundler();

  const server = createServer((req, res) => {
    overlay.handleRequest(req, res).catch((err) => {
      console.error(err);
      text(res, 500, STATUS_CODES[500] || "Error");
    });
  });

  server.on("upgrade", (req, socket, head) => {
    const url = new URL(req.url || "/", `http://${HOST}:${PORT}`);
    if (url.pathname === "/__dev_reload") {
      const client = upgradeWebSocket(req, socket, head, {
        onClose() {
          reloadClients.delete(client);
        }
      });
      if (client) reloadClients.add(client);
      return;
    }
    if (overlay.handleUpgrade(req, socket, head)) return;
    socket.write("HTTP/1.1 404 Not Found\r\n\r\n");
    socket.destroy();
  });

  server.listen(PORT, HOST, () => {
    const base = overlay.baseUrl();
    const selected = overlay.selectedCanvas();
    console.log("");
    console.log("  CCS Overlay Server (Dev)");
    console.log(`  ${base}/`);
    console.log(`  Editor: ${base}/editor/${selected.id}`);
    console.log(`  View:   ${base}/view/${selected.id}`);
    console.log(`  Chat:   ${base}/chat`);
    console.log(`  Health: ${base}/health`);
    console.log(`  Sim:    ${SIM_ENABLED ? "on" : "off"} (CCS_DEV_SIM=0 zum Abschalten)`);
    console.log("");
  });

  if (SIM_ENABLED) {
    createSimulator(overlay.getOverlayData(), overlay.publishApp, {
      persist: () => overlay.persistData()
    }).start(4000);
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
