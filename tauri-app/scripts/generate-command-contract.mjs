import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
const root = new URL("../", import.meta.url);
const source = readFileSync(new URL("src-tauri/src/lib.rs", root), "utf8");
function split(text) {
    let depth = 0,
        start = 0,
        parts = [];
    for (let i = 0; i < text.length; i++) {
        if ("<([{".includes(text[i])) depth++;
        if (">)]}".includes(text[i])) depth--;
        if (text[i] === "," && depth === 0) {
            parts.push(text.slice(start, i).trim());
            start = i + 1;
        }
    }
    return [...parts, text.slice(start).trim()].filter(Boolean);
}
const domainFiles = {
    ObsControl: ["obs/controls.rs", "action"],
    ObsQuery: ["obs/queries.rs", "query"],
    SpotifyAction: ["spotify/playback.rs", "action"],
    SpotifyQuery: ["spotify/playback.rs", "query"],
    TwitchAction: ["twitch/operations.rs", "action"],
    TwitchQuery: ["twitch/operations.rs", "query"],
};
function type(rust) {
    rust = rust.trim();
    const name = rust.split("::").at(-1);
    if (domainFiles[name]) return name;
    if (name === "AlertDefinition" || name === "UpdatePackage") return name;
    if (rust === "String") return "string";
    if (rust === "bool") return "boolean";
    if (/^[ui](8|16|32|64|size)$|^f(32|64)$/.test(rust)) return "number";
    if (rust.startsWith("Option<")) return type(rust.slice(7, -1)) + " | null";
    if (rust.startsWith("Vec<")) return `Array<${type(rust.slice(4, -1))}>`;
    return "unknown";
}
const commands = [];
for (const match of source.matchAll(
    /#\[tauri::command\]\s*(?:async\s+)?fn\s+(\w+)(?:<[^\n]*>)?\s*\(([\s\S]*?)\)\s*(?:->|\{)/g,
)) {
    const args = split(match[2])
        .map((p) => {
            const m = p.match(/^(\w+)\s*:\s*([\s\S]+)$/);
            if (!m) throw Error("Unrecognized parameter " + p);
            return m.slice(1);
        })
        .filter(
            ([, t]) => !t.startsWith("State<") && !t.startsWith("AppHandle"),
        );
    const fields = args.map(
        ([k, t]) =>
            `${k.replace(/_([a-z])/g, (_, c) => c.toUpperCase())}${t.startsWith("Option<") ? "?" : ""}: ${type(t)}`,
    );
    commands.push(
        `  | [command: "${match[1]}"${fields.length ? `, args${args.every(([, t]) => t.startsWith("Option<")) ? "?" : ""}: { ${fields.join("; ")} }` : `, args?: Record<string, never>`}]`,
    );
}
const count = (source.match(/#\[tauri::command\]/g) || []).length;
if (count !== commands.length)
    throw Error(`Parsed ${commands.length}/${count} commands`);
const domains = [];
for (const [name, [file, tag]] of Object.entries(domainFiles)) {
    const rust = readFileSync(
        new URL("src-tauri/crates/ccs-modules/src/" + file, root),
        "utf8",
    );
    const start = rust.indexOf("pub enum " + name + " {");
    if (start < 0) throw Error("Missing enum " + name);
    let end = start + ("pub enum " + name + " {").length,
        depth = 1,
        bodyStart = end;
    for (; depth && end < rust.length; end++) {
        if (rust[end] === "{") depth++;
        if (rust[end] === "}") depth--;
    }
    const variants = split(rust.slice(bodyStart, end - 1)).map((v) => {
        const m = v.match(/^(\w+)(?:\s*\{([\s\S]*)\})?$/);
        if (!m) throw Error("Unsupported enum variant " + v);
        const action = m[1].replace(
            /[A-Z]/g,
            (c, i) => (i ? "_" : "") + c.toLowerCase(),
        );
        const fields = m[2]
            ? split(m[2]).map((p) => {
                  const [, key, t] = p.match(/^(\w+)\s*:\s*([\s\S]+)$/) || [];
                  if (!key) throw Error("Unsupported enum field " + p);
                  return (
                      key.replace(/_([a-z])/g, (_, c) => c.toUpperCase()) +
                      (t.startsWith("Option<") ? "?" : "") +
                      ": " +
                      type(t)
                  );
              })
            : [];
        return `  | { ${tag}: "${action}"${fields.length ? "; " + fields.join("; ") : ""} }`;
    });
    domains.push(`export type ${name} =\n${variants.join("\n")};\n`);
}
const output =
    'import type {AlertDefinition, UpdatePackage} from "./api";\n' +
    domains.join("\n") +
    "// Generated from src-tauri/src/lib.rs. Run npm run contracts:generate.\nexport type CommandInvocation =\n" +
    commands.join("\n") +
    ";\n";
const target = new URL("src/lib/command-contract.ts", root);
if (process.argv.includes("--check")) {
    if (readFileSync(target, "utf8").replace(/\r\n/g, "\n") !== output)
        throw Error(
            "Command contract is stale; run npm run contracts:generate",
        );
} else writeFileSync(target, output);
