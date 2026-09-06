#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { verifyAcceptance } = require("../unity/verify-acceptance");

const directory = fs.mkdtempSync(path.join(os.tmpdir(), "unity-acceptance-"));
let passed = 0;
const commit = "a".repeat(40);
const version = "2021.3.45f1";
function evidence(reference = 20, subject = 12.5) {
  const ratio = reference / subject;
  // Constant observations have exactly this ratio for every possible bootstrap resample.
  return {
    EnvironmentMetadata: {
      commit,
      unityVersion: version,
      backend: "IL2CPP",
      isEditor: "False",
      buildConfiguration: "Release"
    },
    ReferenceMilliseconds: Array(32).fill(reference),
    SubjectMilliseconds: Array(32).fill(subject),
    PairedLogRatios: Array(32).fill(Math.log(reference) - Math.log(subject)),
    Comparison: { Ratio: ratio, ReferenceSpread: 0, SubjectSpread: 0, Cycles: 32 },
    ReferenceSummary: { Median: reference, P95: reference, MedianAbsoluteDeviation: 0 },
    SubjectSummary: { Median: subject, P95: subject, MedianAbsoluteDeviation: 0 },
    RatioLower95: ratio,
    RatioUpper95: ratio,
    HasSufficientTiming: reference >= 10 && subject >= 10,
    Iterations: 1,
    Seed: 1,
    ReferenceWarmupMilliseconds: 100,
    SubjectWarmupMilliseconds: 100,
    ReferenceWarmupExecutions: 3,
    SubjectWarmupExecutions: 3
  };
}
function variedEvidence() {
  const value = evidence();
  value.Seed = -1640531527;
  value.ReferenceMilliseconds = Array.from(
    { length: 32 },
    (_, i) => 20 + (i % 4) * 0.02 + Math.floor(i / 4) * 0.005
  );
  value.SubjectMilliseconds = Array.from({ length: 32 }, (_, i) => 12.5 + ((i * 7) % 5) * 0.003);
  value.PairedLogRatios = value.ReferenceMilliseconds.map(
    (r, i) => Math.log(r) - Math.log(value.SubjectMilliseconds[i])
  );
  // Independently calculated with Python integer uint32 draws and all 2000 whole-batch resamples.
  value.Comparison = {
    Ratio: 1.6030534127646099,
    ReferenceSpread: 0.004750000000000032,
    SubjectSpread: 0.0009600000000000719,
    Cycles: 32
  };
  value.RatioLower95 = 1.602415896115002;
  value.RatioUpper95 = 1.6036998625535464;
  value.ReferenceSummary = {
    Median: 20.0475,
    P95: 20.087249999999997,
    MedianAbsoluteDeviation: 0.019999999999999574
  };
  value.SubjectSummary = {
    Median: 12.506,
    P95: 12.512,
    MedianAbsoluteDeviation: 0.0030000000000001137
  };
  return value;
}
function run(transform = () => {}, lineTransform = (lines) => lines, create = evidence) {
  const lines = [];
  for (const entries of [1000, 10000]) {
    for (const misses of [0, 50]) {
      const sample = create();
      transform(sample, entries, misses);
      lines.push(`INTMAP_PAIRED_SAMPLES ${entries} ${misses} ${JSON.stringify(sample)}`);
    }
  }
  fs.writeFileSync(path.join(directory, "intmap/player.log"), lineTransform(lines).join("\n"));
  return verifyAcceptance(directory, commit, version);
}
function test(name, action) {
  action();
  passed++;
  process.stdout.write(`PASS ${name}\n`);
}
try {
  fs.mkdirSync(path.join(directory, "intmap"));
  test("stable hit margins qualify", () => assert.equal(run().decision, "meets-hit-margin"));
  test("whole-batch signed-seed golden interval agrees", () =>
    assert.equal(run(undefined, undefined, variedEvidence).decision, "meets-hit-margin"));
  test("consistent unstable observations remain inconclusive", () => {
    const create = () => {
      const value = evidence();
      value.ReferenceMilliseconds = Array.from({ length: 32 }, (_, i) => (i % 2 ? 22 : 20));
      value.SubjectMilliseconds = Array.from({ length: 32 }, (_, i) => (i % 2 ? 13.75 : 12.5));
      value.Comparison.ReferenceSpread = 0.1;
      value.Comparison.SubjectSpread = 0.1;
      value.ReferenceSummary = { Median: 21, P95: 22, MedianAbsoluteDeviation: 1 };
      value.SubjectSummary = { Median: 13.125, P95: 13.75, MedianAbsoluteDeviation: 0.625 };
      return value;
    };
    assert.equal(run(undefined, undefined, create).decision, "inconclusive");
  });
  test("short raw durations are inconclusive", () =>
    assert.equal(run(undefined, undefined, () => evidence(8, 5)).decision, "inconclusive"));
  test("stable lower margin is recorded", () =>
    assert.equal(run(undefined, undefined, () => evidence(15, 12.5)).decision, "below-hit-margin"));
  test("slower subject remains below margin", () =>
    assert.equal(run(undefined, undefined, () => evidence(20, 40)).decision, "below-hit-margin"));
  test("small cross-runtime precision differences are accepted", () => {
    const result = run((sample) => {
      sample.Comparison.Ratio += 1e-12;
      sample.RatioLower95 += 1e-12;
      sample.RatioUpper95 += 1e-12;
    });
    assert.equal(result.decision, "meets-hit-margin");
    assert.ok(
      Math.abs(result.workloads["1000/0"].ratio - 1.6) < 1e-13,
      "Output must use raw observations, not the perturbed logged ratio."
    );
  });
  test("forged fast summary cannot override slow raw samples", () =>
    assert.throws(() =>
      run((sample) => {
        sample.SubjectMilliseconds.fill(40);
        sample.Comparison.ReferenceSpread = -1;
        sample.Comparison.SubjectSpread = -1;
      })
    ));
  for (const [name, mutation] of [
    ["wrong commit", (sample) => (sample.EnvironmentMetadata.commit = "b".repeat(40))],
    ["wrong version", (sample) => (sample.EnvironmentMetadata.unityVersion = "6000.5.2f1")],
    ["editor result", (sample) => (sample.EnvironmentMetadata.isEditor = "True")],
    ["Mono result", (sample) => (sample.EnvironmentMetadata.backend = "Mono")],
    [
      "development result",
      (sample) => (sample.EnvironmentMetadata.buildConfiguration = "Development")
    ],
    ["31 observations", (sample) => sample.ReferenceMilliseconds.pop()],
    ["nonfinite observation", (sample) => (sample.SubjectMilliseconds[0] = null)],
    ["missing statistics", (sample) => delete sample.Comparison],
    ["forged ratio", (sample) => (sample.Comparison.Ratio = 2)],
    ["negative spread", (sample) => (sample.Comparison.ReferenceSpread = -1)],
    ["forged stable spread", (sample) => (sample.Comparison.ReferenceSpread = 0.02)],
    ["forged cycle count", (sample) => (sample.Comparison.Cycles = 31)],
    ["missing upper interval", (sample) => delete sample.RatioUpper95],
    ["inverted interval", (sample) => (sample.RatioUpper95 = 1.2)],
    ["forged lower interval", (sample) => (sample.RatioLower95 = 1.1)],
    ["precision mismatch beyond tolerance", (sample) => (sample.Comparison.Ratio += 1e-7)],
    ["forged raw sufficiency flag", (sample) => (sample.HasSufficientTiming = false)],
    ["short reference warmup", (sample) => (sample.ReferenceWarmupMilliseconds = 99)],
    ["short subject warmup", (sample) => (sample.SubjectWarmupMilliseconds = 99)],
    ["insufficient warmup executions", (sample) => (sample.ReferenceWarmupExecutions = 2)],
    ["fractional warmup count", (sample) => (sample.SubjectWarmupExecutions = 3.5)],
    ["zero iterations", (sample) => (sample.Iterations = 0)],
    ["fractional iterations", (sample) => (sample.Iterations = 1.5)],
    ["out of range iterations", (sample) => (sample.Iterations = 2147483648)],
    ["missing seed", (sample) => delete sample.Seed],
    ["out of range seed", (sample) => (sample.Seed = 2147483648)],
    ["forged paired log", (sample) => (sample.PairedLogRatios[0] = 0)],
    ["missing paired logs", (sample) => delete sample.PairedLogRatios],
    ["forged median", (sample) => (sample.ReferenceSummary.Median = 30)],
    ["forged p95", (sample) => (sample.SubjectSummary.P95 = 30)],
    ["forged MAD", (sample) => (sample.SubjectSummary.MedianAbsoluteDeviation = 1)]
  ]) {
    test(name, () => assert.throws(() => run(mutation)));
  }
  test("changed bootstrap seed invalidates varied interval", () =>
    assert.throws(() => run((sample) => (sample.Seed = 1), undefined, variedEvidence)));
  test("missing workload", () => assert.throws(() => run(undefined, (lines) => lines.slice(1))));
  test("duplicate workload", () =>
    assert.throws(() => run(undefined, (lines) => [...lines, lines[0]])));
  test("empty log", () => assert.throws(() => run(undefined, () => [])));
  test("invalid CLI evidence cannot retain a stale passing summary", () => {
    const summary = path.join(directory, "acceptance-summary.json");
    fs.writeFileSync(summary, '{"decision":"meets-hit-margin"}');
    fs.writeFileSync(path.join(directory, "intmap/player.log"), "");
    const result = spawnSync(
      process.execPath,
      [path.join(__dirname, "../unity/verify-acceptance.js"), directory, commit, version],
      { encoding: "utf8" }
    );
    assert.ifError(result.error);
    assert.equal(result.status, 1);
    assert.match(result.stderr, /Expected all four IntMap workloads/);
    assert.equal(fs.existsSync(summary), false);
  });
  process.stdout.write(`${passed} acceptance controls passed.\n`);
} finally {
  fs.rmSync(directory, { recursive: true, force: true });
}
