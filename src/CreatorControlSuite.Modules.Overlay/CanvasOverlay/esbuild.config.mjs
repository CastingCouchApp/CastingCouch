import * as esbuild from "esbuild";
import { mkdirSync, renameSync, existsSync, unlinkSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const watch = process.argv.includes("--watch");

const entries = [
  {
    in: join(__dirname, "src/shared/index.ts"),
    outJs: join(__dirname, "shared/runtime.js"),
    outCss: join(__dirname, "shared/styles.css")
  },
  {
    in: join(__dirname, "src/editor/main.ts"),
    outJs: join(__dirname, "editor/editor.js"),
    outCss: join(__dirname, "editor/editor.css")
  },
  {
    in: join(__dirname, "src/view/main.ts"),
    outJs: join(__dirname, "view/view.js"),
    outCss: null
  },
  {
    in: join(__dirname, "src/solo/main.ts"),
    outJs: join(__dirname, "solo/solo.js"),
    outCss: null
  }
];

for (const e of entries) {
  mkdirSync(dirname(e.outJs), { recursive: true });
  if (e.outCss) mkdirSync(dirname(e.outCss), { recursive: true });
}

function buildOptions(entry) {
  return {
    entryPoints: [entry.in],
    outfile: entry.outJs,
    bundle: true,
    format: "iife",
    platform: "browser",
    target: ["es2020"],
    minify: false,
    keepNames: true,
    sourcemap: false,
    logLevel: "info",
    loader: {
      ".css": "css",
      ".png": "dataurl",
      ".svg": "dataurl"
    }
  };
}

function finalizeCss(entry) {
  if (!entry.outCss) return;
  const autoCss = entry.outJs.replace(/\.js$/i, ".css");
  if (!existsSync(autoCss)) return;
  if (autoCss.toLowerCase() === entry.outCss.toLowerCase()) return;
  if (existsSync(entry.outCss)) {
    try { unlinkSync(entry.outCss); } catch { /* ignore */ }
  }
  renameSync(autoCss, entry.outCss);
}

async function run() {
  if (watch) {
    for (const entry of entries) {
      const ctx = await esbuild.context(buildOptions(entry));
      await ctx.watch();
    }
    console.log("watching…");
    return;
  }

  for (const entry of entries) {
    await esbuild.build(buildOptions(entry));
    finalizeCss(entry);
  }
  console.log("Canvas Overlay bundles built.");
}

run().catch((err) => {
  console.error(err);
  process.exit(1);
});
