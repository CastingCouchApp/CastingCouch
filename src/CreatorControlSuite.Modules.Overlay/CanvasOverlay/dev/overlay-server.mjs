/**
 * Overlay-Webserver-Simulation (gleiche Routen wie OverlayWebServer.cs).
 * Hot-Reload / esbuild bleiben im aufrufenden Dev-Server.
 */
import {
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
  readdirSync,
  statSync
} from "node:fs";
import { dirname, join, extname, resolve, sep, basename } from "node:path";
import {
  SIZE_PRESETS,
  WIDGET_TYPES,
  SHAPE_TYPES,
  defaultLayout,
  defaultCanvases,
  createOverlayData,
  chatConfig,
  makeEvent
} from "./mock-state.mjs";
import { upgradeWebSocket } from "./ws.mjs";

const MIME = {
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".gif": "image/gif",
  ".webp": "image/webp",
  ".map": "application/json",
  ".woff2": "font/woff2",
  ".woff": "font/woff",
  ".ttf": "font/ttf",
  ".otf": "font/otf"
};

/**
 * @param {{
 *   host: string,
 *   port: number,
 *   canvasRoot: string,
 *   chatRoot: string,
 *   dataDir: string,
 *   layoutDir: string,
 *   extensionsDir: string,
 *   injectHtml?: (html: string) => string,
 *   serveCanvasAsset?: (rel: string, res: import('node:http').ServerResponse) => boolean,
 * }} opts
 */
export function createOverlayServer(opts) {
  const {
    host,
    port,
    canvasRoot,
    chatRoot,
    dataDir,
    layoutDir,
    extensionsDir,
    injectHtml = (h) => h,
    serveCanvasAsset
  } = opts;

  mkdirSync(dataDir, { recursive: true });
  mkdirSync(layoutDir, { recursive: true });
  mkdirSync(extensionsDir, { recursive: true });

  const dataPath = join(dataDir, "overlay-data.json");
  const configPath = join(dataDir, "overlay-config.json");
  const canvasesPath = join(dataDir, "canvases.json");

  /** @type {ReturnType<typeof createOverlayData>} */
  let overlayData = loadJson(dataPath, createOverlayData);
  /** @type {ReturnType<typeof defaultCanvases>} */
  let canvasRegistry = loadJson(canvasesPath, defaultCanvases);
  ensureCanvasLayouts(canvasRegistry, layoutDir);

  /** @type {object[]} */
  const chatHistory = [];
  /** @type {Set<{send:(t:string)=>void}>} */
  const appClients = new Set();

  function baseUrl() {
    return `http://${host}:${port}`;
  }

  function persistData() {
    writeFileSync(dataPath, JSON.stringify(overlayData, null, 2), "utf8");
  }

  function persistCanvases() {
    writeFileSync(canvasesPath, JSON.stringify(canvasRegistry, null, 2), "utf8");
  }

  function listCanvases() {
    const byId = new Map(canvasRegistry.canvases.map((c) => [c.id, c]));
    if (existsSync(layoutDir)) {
      for (const file of readdirSync(layoutDir)) {
        if (!file.endsWith(".json")) continue;
        const id = file.slice(0, -5);
        if (!byId.has(id)) {
          byId.set(id, { id, name: id });
        }
      }
    }
    if (!byId.size) {
      byId.set("default", { id: "default", name: "Dev Canvas" });
    }
    return Array.from(byId.values());
  }

  function selectedCanvas() {
    const list = listCanvases();
    return list.find((c) => c.id === canvasRegistry.selectedId) || list[0];
  }

  function widgetUrl(type) {
    return `${baseUrl()}/w/${encodeURIComponent(type)}`;
  }

  function editorUrl(id) {
    return `${baseUrl()}/editor/${encodeURIComponent(id)}`;
  }

  function viewUrl(id) {
    return `${baseUrl()}/view/${encodeURIComponent(id)}`;
  }

  function safeId(id) {
    const s = String(id || "").trim();
    if (!s || s.includes("..") || s.includes("/") || s.includes("\\")) return null;
    return s;
  }

  function layoutPath(id) {
    return join(layoutDir, id + ".json");
  }

  function loadLayout(id) {
    const path = layoutPath(id);
    if (existsSync(path)) {
      return JSON.parse(readFileSync(path, "utf8"));
    }
    const known = canvasRegistry.canvases.find((c) => c.id === id);
    return defaultLayout(known?.name || id);
  }

  function saveLayout(id, layout) {
    if (layout && typeof layout === "object" && !layout.name) {
      const known = canvasRegistry.canvases.find((c) => c.id === id);
      if (known) layout.name = known.name;
    }
    writeFileSync(layoutPath(id), JSON.stringify(layout, null, 2), "utf8");
    if (!canvasRegistry.canvases.some((c) => c.id === id)) {
      canvasRegistry.canvases.push({ id, name: layout?.name || id });
      persistCanvases();
    }
  }

  function publishApp(evt) {
    if (evt?.type === "channel.chat.message" || (evt?.source === "twitch" && String(evt.type || "").startsWith("channel."))) {
      chatHistory.push(evt);
      while (chatHistory.length > 100) chatHistory.shift();
    }
    const raw = JSON.stringify(evt);
    for (const c of appClients) {
      try {
        c.send(raw);
      } catch {
        /* ignore */
      }
    }
  }

  function listExtensionPacks() {
    if (!existsSync(extensionsDir)) return [];
    const packs = [];
    for (const name of readdirSync(extensionsDir)) {
      const packDir = join(extensionsDir, name);
      if (!statSync(packDir).isDirectory()) continue;
      const manifestPath = join(packDir, "manifest.json");
      if (!existsSync(manifestPath)) continue;
      try {
        const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
        const id = String(manifest.id || name);
        packs.push({
          id,
          name: manifest.name || id,
          version: manifest.version || "0.0.0",
          apiVersion: manifest.apiVersion || 1,
          widgets: manifest.widgets || [],
          effects: manifest.effects || [],
          animations: manifest.animations || [],
          fonts: manifest.fonts || [],
          assets: manifest.assets || [],
          baseUrl: `/ext/${id}/`
        });
      } catch {
        /* skip bad manifest */
      }
    }
    return packs;
  }

  function json(res, status, body) {
    const raw = JSON.stringify(body);
    res.writeHead(status, {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store, no-cache, must-revalidate",
      "Content-Length": Buffer.byteLength(raw)
    });
    res.end(raw);
  }

  function text(res, status, body, contentType = "text/plain; charset=utf-8") {
    res.writeHead(status, {
      "Content-Type": contentType,
      "Cache-Control": "no-store, no-cache, must-revalidate",
      "Content-Length": Buffer.byteLength(body)
    });
    res.end(body);
  }

  function notFound(res) {
    text(res, 404, "Not Found");
  }

  function redirect(res, location) {
    res.writeHead(302, { Location: location, "Cache-Control": "no-store" });
    res.end();
  }

  function underDir(root, candidate) {
    const resolved = resolve(candidate);
    const rootWithSep = root.endsWith(sep) ? root : root + sep;
    return resolved === root || resolved.startsWith(rootWithSep);
  }

  function serveFile(res, absPath, { inject = false } = {}) {
    if (!existsSync(absPath) || !statSync(absPath).isFile()) {
      notFound(res);
      return;
    }
    const ext = extname(absPath).toLowerCase();
    const type = MIME[ext] || "application/octet-stream";
    if (inject && ext === ".html") {
      text(res, 200, injectHtml(readFileSync(absPath, "utf8")), type);
      return;
    }
    const buf = readFileSync(absPath);
    res.writeHead(200, {
      "Content-Type": type,
      "Cache-Control": "no-store, no-cache, must-revalidate",
      "Content-Length": buf.length
    });
    res.end(buf);
  }

  function readBody(req) {
    return new Promise((resolveBody, reject) => {
      const chunks = [];
      req.on("data", (c) => chunks.push(c));
      req.on("end", () => resolveBody(Buffer.concat(chunks)));
      req.on("error", reject);
    });
  }

  function healthPayload() {
    const selected = selectedCanvas();
    const canvases = listCanvases();
    return {
      ok: true,
      mode: "browser-dev",
      port,
      root: dataDir,
      clients: appClients.size,
      baseUrl: baseUrl(),
      canvasId: selected.id,
      canvases: canvases.map((c) => ({
        id: c.id,
        name: c.name,
        editorUrl: editorUrl(c.id),
        viewUrl: viewUrl(c.id)
      })),
      editorUrl: editorUrl(selected.id),
      viewUrl: viewUrl(selected.id),
      widgets: [
        ...WIDGET_TYPES.map((type) => ({ type, url: widgetUrl(type) })),
        ...SHAPE_TYPES.map((type) => ({ type, url: widgetUrl("shape/" + type) }))
      ]
    };
  }

  function landingHtml() {
    const health = healthPayload();
    const canvasLinks = health.canvases
      .map(
        (c) =>
          `<a href="${c.editorUrl}">Editor · ${escapeHtml(c.name)} <code>/editor/${escapeHtml(c.id)}</code></a>` +
          `<a href="${c.viewUrl}">View · ${escapeHtml(c.name)} <code>/view/${escapeHtml(c.id)}</code></a>`
      )
      .join("\n");
    const widgetLinks = WIDGET_TYPES.slice(0, 8)
      .map((t) => `<a href="/w/${t}">Solo · ${t}</a>`)
      .join("\n");
    return `<!DOCTYPE html>
<html lang="de">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>CCS Overlay Server (Dev)</title>
  <style>
    :root { color-scheme: dark; font-family: "Segoe UI", system-ui, sans-serif; }
    body { margin: 0; min-height: 100vh; background: #12161c; color: #e8edf5; }
    main { width: min(720px, 92vw); margin: 2.5rem auto 3rem; }
    h1 { font-size: 1.45rem; margin: 0 0 .35rem; }
    h2 { font-size: .95rem; margin: 1.4rem 0 .5rem; color: #9aa7b8; font-weight: 600; }
    p { color: #9aa7b8; margin: 0 0 1rem; line-height: 1.45; }
    a { display: block; padding: .7rem .9rem; margin: .35rem 0; border-radius: 10px; background: #1c2430; color: #9ecbff; text-decoration: none; }
    a:hover { background: #243041; }
    code { color: #c9d4e3; font-size: .85em; }
    .muted { font-size: .85rem; color: #7d8a9a; margin-top: 1.4rem; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: .35rem 1rem; }
    @media (max-width: 640px) { .grid { grid-template-columns: 1fr; } }
  </style>
</head>
<body>
  <main>
    <h1>CCS Overlay Server · Dev</h1>
    <p>Simuliert den lokalen Overlay-Webserver (Port ${port}): Layout-API, Daten, Chat, WS-Events, Solo-Widgets. Zusätzlich Hot-Reload für Canvas-Bundles.</p>
    <h2>Canvases</h2>
    ${canvasLinks}
    <h2>Standalone</h2>
    <a href="/chat">Chat Overlay · /chat</a>
    <div class="grid">${widgetLinks}</div>
    <h2>API</h2>
    <a href="/health">Health · /health</a>
    <a href="/data/overlay-data.json">Overlay-Daten · /data/overlay-data.json</a>
    <a href="/extensions">Extensions · /extensions</a>
    <p class="muted">Daten: <code>dev/.data/</code> · Layouts: <code>dev/.layouts/</code> · Packs: <code>dev/extensions/</code></p>
  </main>
</body>
</html>`;
  }

  async function handleApi(req, res, url) {
    const path = url.pathname;

    if (path === "/health") {
      json(res, 200, healthPayload());
      return true;
    }

    if (path === "/data/overlay-data.json") {
      if (req.method === "PUT" || req.method === "POST") {
        try {
          const raw = await readBody(req);
          overlayData = Object.assign(overlayData, JSON.parse(raw.toString("utf8") || "{}"));
          overlayData.updatedAt = new Date().toISOString();
          persistData();
          json(res, 200, overlayData);
        } catch (err) {
          json(res, 400, { error: String(err && err.message ? err.message : err) });
        }
        return true;
      }
      json(res, 200, overlayData);
      return true;
    }

    if (path === "/data/overlay-config.json") {
      if (existsSync(configPath)) {
        serveFile(res, configPath);
      } else {
        json(res, 200, {});
      }
      return true;
    }

    if (path === "/chat/config") {
      json(res, 200, chatConfig());
      return true;
    }

    if (path === "/chat/history") {
      json(res, 200, { events: chatHistory.slice() });
      return true;
    }

    if (path === "/chat/background") {
      notFound(res);
      return true;
    }

    if (path === "/canvas/size-presets") {
      json(res, 200, SIZE_PRESETS);
      return true;
    }

    if (path === "/obs/video-settings") {
      const connected = overlayData?.obs?.connected !== false;
      json(res, 200, connected
        ? {
            connected: true,
            baseWidth: 1920,
            baseHeight: 1080,
            outputWidth: 1920,
            outputHeight: 1080
          }
        : {
            connected: false,
            baseWidth: 0,
            baseHeight: 0,
            outputWidth: 0,
            outputHeight: 0
          });
      return true;
    }

    if (path === "/obs/preview") {
      // 1×1 transparent PNG — Editor behandelt 503/leer als „keine Vorschau“
      res.writeHead(503, { "Cache-Control": "no-store" });
      res.end();
      return true;
    }

    if (path === "/extensions") {
      json(res, 200, { packs: listExtensionPacks() });
      return true;
    }

    // Manuelles Event injizieren (nur Dev)
    if (path === "/dev/event" && (req.method === "POST" || req.method === "PUT")) {
      try {
        const raw = await readBody(req);
        const body = JSON.parse(raw.toString("utf8") || "{}");
        const event = makeEvent(
          body.source || "app",
          body.type || "app.alert",
          body.summary || body.type || "dev event",
          body.data || {}
        );
        publishApp(event);
        json(res, 200, { ok: true, event });
      } catch (err) {
        json(res, 400, { error: String(err && err.message ? err.message : err) });
      }
      return true;
    }

    if (path === "/dev/canvases" && (req.method === "POST" || req.method === "PUT")) {
      try {
        const raw = await readBody(req);
        const body = JSON.parse(raw.toString("utf8") || "{}");
        if (Array.isArray(body.canvases)) {
          canvasRegistry.canvases = body.canvases;
        }
        if (body.selectedId) {
          canvasRegistry.selectedId = String(body.selectedId);
        }
        persistCanvases();
        ensureCanvasLayouts(canvasRegistry, layoutDir);
        json(res, 200, canvasRegistry);
      } catch (err) {
        json(res, 400, { error: String(err && err.message ? err.message : err) });
      }
      return true;
    }

    const layoutMatch = path.match(/^\/layout\/([^/]+)$/);
    if (layoutMatch) {
      const id = safeId(decodeURIComponent(layoutMatch[1]));
      if (!id) {
        json(res, 400, { error: "invalid instance id" });
        return true;
      }
      if (req.method === "GET") {
        json(res, 200, loadLayout(id));
        return true;
      }
      if (req.method === "PUT") {
        try {
          const raw = await readBody(req);
          const layout = JSON.parse(raw.toString("utf8") || "{}");
          saveLayout(id, layout);
          publishApp({
            source: "app",
            type: "app.overlay.layout",
            at: new Date().toISOString(),
            summary: `Layout: ${id}`,
            data: { instanceId: id, layout: JSON.stringify(layout) }
          });
          json(res, 200, layout);
        } catch (err) {
          json(res, 400, { error: String(err && err.message ? err.message : err) });
        }
        return true;
      }
    }

    return false;
  }

  async function handleRequest(req, res) {
    const url = new URL(req.url || "/", baseUrl());
    if (await handleApi(req, res, url)) return;

    const path = url.pathname;

    if (path === "/" || path === "/index.html") {
      text(res, 200, injectHtml(landingHtml()), "text/html; charset=utf-8");
      return;
    }

    if (path === "/editor") {
      redirect(res, "/editor/" + encodeURIComponent(selectedCanvas().id));
      return;
    }
    if (path.startsWith("/editor/")) {
      serveFile(res, join(canvasRoot, "editor", "index.html"), { inject: true });
      return;
    }

    if (path === "/view") {
      redirect(res, "/view/" + encodeURIComponent(selectedCanvas().id));
      return;
    }
    if (path.startsWith("/view/")) {
      serveFile(res, join(canvasRoot, "view", "index.html"), { inject: true });
      return;
    }

    if (path.startsWith("/w/") || path === "/w") {
      serveFile(res, join(canvasRoot, "solo", "index.html"), { inject: true });
      return;
    }

    if (path === "/chat") {
      serveFile(res, join(chatRoot, "index.html"), { inject: true });
      return;
    }

    if (path.startsWith("/chat/")) {
      const fileName = basename(path);
      if (["config", "background", "history"].includes(fileName)) {
        notFound(res);
        return;
      }
      const abs = join(chatRoot, fileName);
      if (!underDir(chatRoot, abs)) {
        notFound(res);
        return;
      }
      serveFile(res, abs);
      return;
    }

    if (path.startsWith("/canvas/")) {
      const rel = path.slice("/canvas/".length);
      if (serveCanvasAsset && serveCanvasAsset(rel, res)) return;
      if (rel === "shared/styles.css") {
        const styles = join(canvasRoot, "shared", "styles.css");
        const runtimeCss = join(canvasRoot, "shared", "runtime.css");
        serveFile(res, existsSync(styles) ? styles : runtimeCss);
        return;
      }
      const abs = join(canvasRoot, rel);
      if (!underDir(canvasRoot, abs)) {
        notFound(res);
        return;
      }
      serveFile(res, abs);
      return;
    }

    if (path.startsWith("/ext/")) {
      const rest = path.slice("/ext/".length);
      const slash = rest.indexOf("/");
      if (slash <= 0) {
        notFound(res);
        return;
      }
      const packId = safeId(decodeURIComponent(rest.slice(0, slash)));
      const fileRel = rest.slice(slash + 1);
      if (!packId || !fileRel || fileRel.includes("..")) {
        notFound(res);
        return;
      }
      const packDir = join(extensionsDir, packId);
      const abs = join(packDir, fileRel);
      if (!underDir(packDir, abs)) {
        notFound(res);
        return;
      }
      serveFile(res, abs);
      return;
    }

    notFound(res);
  }

  function handleUpgrade(req, socket, head) {
    const url = new URL(req.url || "/", baseUrl());
    if (url.pathname !== "/ws") return false;

    const client = upgradeWebSocket(req, socket, head, {
      onMessage(raw) {
        try {
          const msg = JSON.parse(raw);
          const type = msg && msg.type;
          if (
            (type === "editor.layout.set" || type === "editor.layout.patch") &&
            msg.data
          ) {
            const id = safeId(msg.data.instanceId);
            if (!id) return;
            const layout =
              typeof msg.data.layout === "string"
                ? JSON.parse(msg.data.layout)
                : msg.data.layout;
            saveLayout(id, layout);
            publishApp({
              source: "app",
              type: "app.overlay.layout",
              at: new Date().toISOString(),
              summary: `Layout: ${id}`,
              data: { instanceId: id, layout: JSON.stringify(layout) }
            });
          }
        } catch {
          /* ignore */
        }
      },
      onClose() {
        appClients.delete(client);
      }
    });

    if (!client) return true;

    appClients.add(client);
    const canvases = listCanvases();
    const helloData = { clients: String(appClients.size) };
    canvases.forEach((c, i) => {
      helloData[`overlay.${i}.id`] = c.id;
      helloData[`overlay.${i}.name`] = c.name;
    });
    client.send(
      JSON.stringify({
        source: "app",
        type: "app.ws.hello",
        at: new Date().toISOString(),
        summary: "connected",
        data: helloData
      })
    );
    for (const evt of chatHistory.slice(-20)) {
      client.send(JSON.stringify(evt));
    }
    return true;
  }

  return {
    handleRequest,
    handleUpgrade,
    publishApp,
    getOverlayData: () => overlayData,
    persistData,
    clientCount: () => appClients.size,
    baseUrl,
    selectedCanvas
  };
}

function loadJson(path, factory) {
  if (existsSync(path)) {
    try {
      return Object.assign(factory(), JSON.parse(readFileSync(path, "utf8")));
    } catch {
      /* fall through */
    }
  }
  const value = factory();
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(value, null, 2), "utf8");
  return value;
}

function ensureCanvasLayouts(registry, layoutDir) {
  mkdirSync(layoutDir, { recursive: true });
  for (const canvas of registry.canvases || []) {
    const path = join(layoutDir, canvas.id + ".json");
    if (!existsSync(path)) {
      writeFileSync(path, JSON.stringify(defaultLayout(canvas.name || canvas.id), null, 2), "utf8");
    }
  }
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
