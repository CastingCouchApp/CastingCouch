import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";

const script = path.resolve("build/Test-CriticalCoverage.mjs");
const criticalFile =
  "src/CreatorControlSuite.Core/Updates/FileUpdateTransaction.cs";

function run({ covered, valid, changedFiles = [criticalFile] }) {
  const directory = fs.mkdtempSync(
    path.join(os.tmpdir(), "ccs-critical-coverage-"));
  const coveragePath = path.join(directory, "coverage.xml");
  const policyPath = path.join(directory, "policy.json");
  const changedPath = path.join(directory, "changed.txt");
  fs.writeFileSync(
    coveragePath,
    `<?xml version="1.0"?>
<coverage>
  <packages><package><classes>
    <class filename="${criticalFile}">
      <lines>
        <line number="10" hits="1" branch="True" condition-coverage="${Math.round(covered / valid * 100)}% (${covered}/${valid})" />
      </lines>
    </class>
  </classes></package></packages>
</coverage>`);
  fs.writeFileSync(
    policyPath,
    JSON.stringify({
      minimumBranchRate: 0.9,
      criticalRoots: [
        "src/CreatorControlSuite.Core/Updates/",
        "src/CreatorControlSuite.Core/Security/",
        "src/CreatorControlSuite.Agent/Security/"
      ],
      enforcedFiles: [criticalFile]
    }));
  fs.writeFileSync(changedPath, changedFiles.join("\n"));

  const result = spawnSync(
    process.execPath,
    [script, coveragePath, policyPath, changedPath],
    { encoding: "utf8" });
  fs.rmSync(directory, { recursive: true, force: true });
  return result;
}

test("accepts enforced critical file at threshold", () => {
  const result = run({ covered: 9, valid: 10 });

  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /90\.00%/);
});

test("rejects enforced critical file below threshold", () => {
  const result = run({ covered: 8, valid: 10 });

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /unter 90\.00%/);
});

test("rejects changed critical file missing from policy", () => {
  const result = run({
    covered: 10,
    valid: 10,
    changedFiles: [
      criticalFile,
      "src/CreatorControlSuite.Core/Security/NewSecretPolicy.cs"
    ]
  });

  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /NewSecretPolicy\.cs/);
});

test("ignores changed files outside critical roots", () => {
  const result = run({
    covered: 10,
    valid: 10,
    changedFiles: [
      criticalFile,
      "src/CreatorControlSuite.App/Views/Example.cs"
    ]
  });

  assert.equal(result.status, 0, result.stderr);
});
