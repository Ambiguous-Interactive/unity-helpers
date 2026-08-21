// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Contract test for the PractRand expectations manifest and its evaluator.
// PractRand's RNG_test exits 0 whether or not it found a failure, so every
// verdict here depends on report parsing being right.

import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const manifestPath = path.join(repoRoot, "scripts", "random-quality", "expected-outcomes.json");
const { classifyReport, evaluate, parseLength } = await import(
  path.join(repoRoot, "scripts", "random-quality", "evaluate-outcomes.mjs")
);

const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));

// The manifest must describe exactly the host's inventory. The workflow enforces
// this against a live --list; here we assert the shape stays coherent offline.
assert.ok(manifest.generators.length > 0, "the manifest must list generators");
assert.equal(manifest.battery.name, "PractRand");
assert.match(
  manifest.battery.sha256,
  /^[0-9a-f]{64}$/,
  "the battery archive must be pinned by SHA-256"
);
assert.match(manifest.measurement.seed, /^[0-9a-f-]{36}$/);

// The manifest mirrors the host's --list order (case-insensitive by name, the
// order Program.cs declares) so a manifest diff lines up with an inventory diff.
const names = manifest.generators.map((generator) => generator.name);
const inHostOrder = [...names].sort((left, right) =>
  left.toLowerCase() < right.toLowerCase() ? -1 : left.toLowerCase() > right.toLowerCase() ? 1 : 0
);
assert.deepEqual(names, inHostOrder, "manifest generators must stay in the host's --list order");
assert.equal(new Set(names).size, names.length, "manifest generators must be unique");

for (const generator of manifest.generators) {
  assert.ok(["pass", "fail"].includes(generator.expected), `${generator.name} needs pass/fail`);
  assert.ok(generator.reason && generator.reason.length > 0, `${generator.name} needs a reason`);
  if (generator.expected === "pass") {
    assert.ok(!("failsBy" in generator), `${generator.name} passes, so it must not carry failsBy`);
  } else if (generator.failsBy !== null) {
    assert.doesNotThrow(
      () => parseLength(generator.failsBy),
      `${generator.name} failsBy must parse`
    );
  }
}

// Length parsing uses PractRand's power-of-two units.
assert.equal(parseLength("1GB"), 1073741824);
assert.equal(parseLength("8GB"), 8589934592);
assert.equal(parseLength("64KB"), 65536);
assert.throws(() => parseLength("1 gigabyte"));

// Only the literal FAIL evaluation counts. "VERY SUSPICIOUS" and "unusual" are
// anomalies PractRand deliberately distinguishes from a failure.
const clean = `rng=RNG_stdin32, seed=unknown
length= 1 gigabyte (2^30 bytes), time= 15.4 seconds
  no anomalies in 156 test result(s)`;
assert.equal(classifyReport(clean).observed, "pass");
assert.equal(classifyReport(clean).lastLength, "1 gigabyte (2^30 bytes)");

const suspicious = `length= 4 kilobytes (2^12 bytes), time= 0.2 seconds
  DC6-9x1Bytes-1                    R=  +7.5  p =  5.6e-3   unusual
  BCFN(2+1,13-9,T)                  R= +43.5  p =  6.7e-11   VERY SUSPICIOUS`;
assert.equal(classifyReport(suspicious).observed, "pass", "suspicion is not failure");

const failing = `length= 16 megabytes (2^24 bytes), time= 9.3 seconds
  [Low1/32]BRank(12):256(1)         R= +2650  p~=  9.8e-799   FAIL !!!!!!!`;
const failed = classifyReport(failing);
assert.equal(failed.observed, "fail");
assert.equal(failed.failures.length, 1);
assert.equal(failed.failures[0].length, "16 megabytes (2^24 bytes)");

function reportsFor(overrides) {
  const reports = new Map();
  for (const generator of manifest.generators) {
    const wanted = overrides[generator.name] ?? generator.expected;
    reports.set(generator.name, wanted === "fail" ? failing : clean);
  }
  return reports;
}

function statusOf(results, name) {
  return results.find((result) => result.name === name).status;
}

// A run that matches the manifest at a budget large enough for every control.
const matching = evaluate(manifest, reportsFor({}), "32GB");
assert.ok(
  matching.every((result) => result.status === "ok" || result.status === "inconclusive"),
  "a manifest-matching run must not report mismatches"
);

// A generator expected to pass that failed is a statistical regression.
const regressed = evaluate(manifest, reportsFor({ PcgRandom: "fail" }), "8GB");
assert.equal(statusOf(regressed, "PcgRandom"), "error");

// An expected-failure control that passed at or beyond its known failing length
// is evidence the harness is broken, and must fail the run.
const brokenHarness = evaluate(manifest, reportsFor({ XorShiftRandom: "pass" }), "1GB");
assert.equal(statusOf(brokenHarness, "XorShiftRandom"), "error");
assert.match(brokenHarness.find((r) => r.name === "XorShiftRandom").detail, /harness is broken/);

// The same control passing below its known failing length is merely
// inconclusive: the run was too short to see the failure.
const tooShort = evaluate(manifest, reportsFor({ SystemRandom: "pass" }), "1GB");
assert.equal(statusOf(tooShort, "SystemRandom"), "inconclusive");
const longEnough = evaluate(manifest, reportsFor({ SystemRandom: "pass" }), "8GB");
assert.equal(statusOf(longEnough, "SystemRandom"), "error");

// A control with no measured failing length can never be asserted.
const unmeasured = manifest.generators.filter(
  (generator) => generator.expected === "fail" && generator.failsBy === null
);
for (const generator of unmeasured) {
  const results = evaluate(manifest, reportsFor({ [generator.name]: "pass" }), "32GB");
  assert.equal(
    statusOf(results, generator.name),
    "inconclusive",
    `${generator.name} has no failsBy, so a pass must never be a mismatch`
  );
}

// The manifest restates each generator's quality rating, and a restated fact drifts.
// Both halves of this session's evidence -- the per-PR linearity gate and this battery --
// exist to make the rating falsifiable, so a rating the manifest disagrees with, or a
// generator rated Good or better that the manifest expects to FAIL, is a contradiction
// the repository should not be able to hold.
const qualityOrder = ["Unknown", "Excellent", "VeryGood", "Good", "Fair", "Poor", "Experimental"];
const randomSourceRoot = path.join(repoRoot, "Runtime", "Core", "Random");
for (const generator of manifest.generators) {
  const source = path.join(randomSourceRoot, `${generator.name}.cs`);
  assert.ok(fs.existsSync(source), `${generator.name} has no source at ${source}`);
  const declared = /\[RandomGeneratorMetadata\(\s*RandomQuality\.(\w+)/.exec(
    fs.readFileSync(source, "utf8")
  );
  assert.ok(declared, `${generator.name} declares no [RandomGeneratorMetadata]`);
  assert.equal(
    generator.quality,
    declared[1],
    `${generator.name}: the manifest says ${generator.quality} but its attribute says ${declared[1]}`
  );

  const rank = qualityOrder.indexOf(generator.quality);
  assert.notEqual(rank, -1, `${generator.name} has an unknown quality ${generator.quality}`);
  if (generator.expected === "fail") {
    assert.ok(
      rank > qualityOrder.indexOf("Good"),
      `${generator.name} is rated ${generator.quality} but is an expected-failure control. ` +
        `Either the rating is too generous or the expectation is wrong -- they cannot both stand.`
    );
  }
}

// A missing report is an error, never a silent pass.
const partial = reportsFor({});
partial.delete("PcgRandom");
assert.equal(statusOf(evaluate(manifest, partial, "8GB"), "PcgRandom"), "error");

// Everything above imports the helpers directly, which leaves the command-line path -- the only
// path the workflow actually uses -- unexercised. A direct-run guard that silently fails produces
// no verdict at all, and a step that prints nothing reads exactly like a clean battery, so the CLI
// is spawned here rather than trusted.
{
  const reportsDir = fs.mkdtempSync(path.join(os.tmpdir(), "random-quality-cli-"));
  try {
    for (const [name, text] of reportsFor({})) {
      fs.writeFileSync(path.join(reportsDir, `${name}.txt`), text, "utf8");
    }

    const run = spawnSync(
      process.execPath,
      [
        "scripts/random-quality/evaluate-outcomes.mjs",
        "--reports",
        reportsDir,
        "--budget",
        "32GB",
        "--seed",
        manifest.measurement.seed
      ],
      { cwd: repoRoot, encoding: "utf8" }
    );

    assert.match(
      run.stdout,
      /mismatch-count=0/,
      `the CLI produced no clean verdict. stdout: ${run.stdout} stderr: ${run.stderr}`
    );
    assert.equal(run.status, 0, `a manifest-matching run must exit 0, got ${run.status}`);
    for (const generator of manifest.generators) {
      assert.match(
        run.stdout,
        new RegExp(generator.name),
        `${generator.name} is missing from the CLI summary`
      );
    }
  } finally {
    fs.rmSync(reportsDir, { recursive: true, force: true });
  }
}

console.log(
  `random-quality outcomes contract: ${manifest.generators.length} generators ` +
    `(${manifest.generators.filter((g) => g.expected === "pass").length} pass, ` +
    `${manifest.generators.filter((g) => g.expected === "fail").length} expected-failure controls) OK`
);
