import fs from "node:fs";

function normalizePath(value) {
  return value.trim().replaceAll("\\", "/").replace(/^\.\/+/, "");
}

function readJson(path) {
  if (!fs.existsSync(path)) {
    throw new Error(`Datei nicht gefunden: ${path}`);
  }

  return JSON.parse(fs.readFileSync(path, "utf8"));
}

function classBlocks(xml, fileName) {
  const blocks = [];
  const classPattern =
    /<class\b([^>]*)>([\s\S]*?)<\/class>/g;
  for (const match of xml.matchAll(classPattern)) {
    const fileMatch = match[1].match(/\bfilename="([^"]+)"/);
    if (fileMatch &&
        normalizePath(fileMatch[1]) === normalizePath(fileName)) {
      blocks.push(match[2]);
    }
  }

  return blocks;
}

function branchCoverage(xml, fileName) {
  const blocks = classBlocks(xml, fileName);
  if (blocks.length === 0) {
    throw new Error(
      `Kritische Datei fehlt im Coverage-Bericht: ${fileName}`);
  }

  let covered = 0;
  let valid = 0;
  for (const block of blocks) {
    for (const line of block.matchAll(/<line\b([^>]*)\/?>/g)) {
      if (!/\bbranch="(?:True|true)"/.test(line[1])) {
        continue;
      }

      const condition = line[1].match(
        /\bcondition-coverage="[^"]*\((\d+)\/(\d+)\)"/);
      if (condition) {
        covered += Number.parseInt(condition[1], 10);
        valid += Number.parseInt(condition[2], 10);
      }
    }
  }

  return {
    covered,
    valid,
    rate: valid === 0 ? 1 : covered / valid
  };
}

function percent(value) {
  return `${(value * 100).toFixed(2)}%`;
}

function main() {
  const [
    coveragePath,
    policyPath = "build/coverage-critical.json",
    changedFilesPath
  ] = process.argv.slice(2);
  if (!coveragePath) {
    throw new Error(
      "Aufruf: node build/Test-CriticalCoverage.mjs "
      + "<coverage.xml> [policy.json] [changed-files.txt]");
  }

  if (!fs.existsSync(coveragePath)) {
    throw new Error(`Coverage-Datei nicht gefunden: ${coveragePath}`);
  }

  const policy = readJson(policyPath);
  const minimum = Number(policy.minimumBranchRate);
  if (!Number.isFinite(minimum) || minimum < 0 || minimum > 1) {
    throw new Error("minimumBranchRate muss zwischen 0 und 1 liegen.");
  }

  const criticalRoots = (policy.criticalRoots ?? []).map(normalizePath);
  const enforcedFiles = (policy.enforcedFiles ?? []).map(entry => {
    if (typeof entry === "string") {
      return { path: normalizePath(entry), minimumBranchRate: minimum };
    }

    return {
      path: normalizePath(entry.path),
      minimumBranchRate:
        Number(entry.minimumBranchRate ?? minimum)
    };
  });
  const enforcedPaths = new Set(enforcedFiles.map(entry => entry.path));

  if (changedFilesPath && fs.existsSync(changedFilesPath)) {
    const changedCriticalFiles = fs.readFileSync(changedFilesPath, "utf8")
      .split(/\r?\n/)
      .map(normalizePath)
      .filter(Boolean)
      .filter(file =>
        file.endsWith(".cs") &&
        criticalRoots.some(root => file.startsWith(root)));
    const unlisted = changedCriticalFiles.filter(
      file => !enforcedPaths.has(file));
    if (unlisted.length > 0) {
      throw new Error(
        "Geänderte kritische Dateien fehlen in coverage-critical.json:\n"
        + unlisted.join("\n"));
    }
  }

  const xml = fs.readFileSync(coveragePath, "utf8");
  for (const entry of enforcedFiles) {
    const result = branchCoverage(xml, entry.path);
    console.log(
      `${entry.path}: Branches ${result.covered}/${result.valid} `
      + `(${percent(result.rate)}), Minimum `
      + percent(entry.minimumBranchRate));
    if (result.rate < entry.minimumBranchRate) {
      throw new Error(
        `${entry.path}: Branch-Coverage ${percent(result.rate)} `
        + `ist unter ${percent(entry.minimumBranchRate)}.`);
    }
  }
}

try {
  main();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}
