#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const {
  auditEntries,
  parseArguments,
  validateManifest
} = require("../check-reference-upstream-drift.js");

const repoRoot = path.resolve(__dirname, "..", "..");
const manifestPath = path.join(repoRoot, "scripts", "reference-upstreams.json");
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
const entries = validateManifest(manifest, repoRoot);

assert.deepEqual(
  new Set(entries.map((entry) => entry.kind)),
  new Set(["sort", "random"]),
  "the live manifest must cover both owner-requested audit families"
);

assert.throws(
  () => validateManifest({ implementations: [entries[0], entries[0]] }, repoRoot),
  /Duplicate implementation name/,
  "duplicate names must not make one upstream silently shadow another"
);

assert.throws(
  () => validateManifest({ implementations: [{ ...entries[0], sha256: "short" }, entries[1]] }),
  /lowercase SHA-256/,
  "a partial checksum must not be accepted as a pin"
);

const parsed = parseArguments(["--manifest", manifestPath, "--report", "report.json"]);
assert.equal(parsed.manifestPath, manifestPath);
assert.equal(parsed.reportPath, path.resolve("report.json"));
assert.throws(() => parseArguments(["--unknown"]), /Unknown or incomplete argument/);

const cliResult = spawnSync(
  process.execPath,
  [path.join(repoRoot, "scripts", "check-reference-upstream-drift.js"), "--unknown"],
  { cwd: repoRoot, encoding: "utf8" }
);
assert.notEqual(cliResult.status, 0, "the CLI must reject an unknown argument");
assert.match(cliResult.stderr, /Unknown or incomplete argument/);

async function run() {
  const expectedBytes = Buffer.from("known upstream source", "utf8");
  const expectedHash = crypto.createHash("sha256").update(expectedBytes).digest("hex");
  const fixture = entries.map((entry) => ({ ...entry, sha256: expectedHash }));

  const current = await auditEntries(fixture, async () => expectedBytes);
  assert.equal(current.passed, true);
  assert.deepEqual(
    current.checks.map((check) => check.status),
    ["current", "current"]
  );

  const drifted = await auditEntries(fixture, async (_url, entry) =>
    entry.name === fixture[0].name ? Buffer.from("changed", "utf8") : expectedBytes
  );
  assert.equal(drifted.passed, false);
  assert.deepEqual(
    drifted.checks.map((check) => check.status),
    ["drifted", "current"]
  );

  const unavailable = await auditEntries(fixture, async () => {
    throw new Error("network unavailable");
  });
  assert.equal(unavailable.passed, false);
  assert.deepEqual(
    unavailable.checks.map((check) => check.status),
    ["unavailable", "unavailable"]
  );
  assert.match(unavailable.checks[0].error, /network unavailable/);

  console.log("[reference-upstreams-test] 13 checks passed.");
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
