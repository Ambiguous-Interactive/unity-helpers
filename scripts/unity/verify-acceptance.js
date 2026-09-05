#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const SAMPLE_COUNT = 32;
const BATCH_SIZE = 4;
const BOOTSTRAP_REPETITIONS = 2000;
const MINIMUM_SAMPLE_MS = 10;

function quantile(sorted, fraction) {
  const position = (sorted.length - 1) * fraction;
  const lower = Math.floor(position);
  const upper = Math.min(lower + 1, sorted.length - 1);
  return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
}

function closeToLogged(actual, expected, label) {
  // CLR and JavaScript transcendental functions can differ in their last digits; raw-derived decisions
  // never use this tolerance. It only checks that logged statistics describe these observations.
  const tolerance = 1e-12 + 1e-10 * Math.abs(expected);
  if (
    !Number.isFinite(actual) ||
    !Number.isFinite(expected) ||
    Math.abs(actual - expected) > tolerance
  ) {
    throw new Error(`${label}: logged statistic disagrees with raw observations.`);
  }
}

function verifyStatistics(evidence, key) {
  const reference = evidence.ReferenceMilliseconds;
  const subject = evidence.SubjectMilliseconds;
  if (
    !Number.isInteger(evidence.Iterations) ||
    evidence.Iterations < 1 ||
    evidence.Iterations > 2147483647 ||
    !Number.isInteger(evidence.Seed) ||
    evidence.Seed < -2147483648 ||
    evidence.Seed > 2147483647
  ) {
    throw new Error(`IntMap ${key}: invalid iteration count or signed 32-bit seed.`);
  }
  for (const arm of ["Reference", "Subject"]) {
    const duration = evidence[`${arm}WarmupMilliseconds`];
    const executions = evidence[`${arm}WarmupExecutions`];
    if (
      !Number.isFinite(duration) ||
      duration < 100 ||
      !Number.isInteger(executions) ||
      executions < 3 ||
      executions > 2147483647
    ) {
      throw new Error(`IntMap ${key}: each arm needs at least 100ms and three warmup executions.`);
    }
  }
  const logs = reference.map((value, index) => Math.log(value) - Math.log(subject[index]));
  const ratio = Math.exp(logs.reduce((sum, value) => sum + value, 0) / SAMPLE_COUNT);
  const spread = (samples) => Math.max(...samples) / Math.min(...samples) - 1;
  const referenceSpread = spread(reference);
  const subjectSpread = spread(subject);
  const sufficient = [...reference, ...subject].every((value) => value >= MINIMUM_SAMPLE_MS);
  const comparison = evidence.Comparison;
  if (
    !comparison ||
    comparison.Cycles !== SAMPLE_COUNT ||
    evidence.HasSufficientTiming !== sufficient
  ) {
    throw new Error(`IntMap ${key}: inconsistent cycle count or timing sufficiency.`);
  }
  closeToLogged(comparison.Ratio, ratio, `${key} ratio`);
  for (const [arm, derivedSpread, samples] of [
    ["Reference", referenceSpread, reference],
    ["Subject", subjectSpread, subject]
  ]) {
    if (comparison[`${arm}Spread`] < 0) throw new Error(`IntMap ${key}: negative spread.`);
    closeToLogged(comparison[`${arm}Spread`], derivedSpread, `${key} ${arm} spread`);
    const sorted = [...samples].sort((a, b) => a - b);
    const median = quantile(sorted, 0.5);
    const deviations = samples.map((value) => Math.abs(value - median)).sort((a, b) => a - b);
    const summary = evidence[`${arm}Summary`];
    if (!summary) throw new Error(`IntMap ${key}: missing ${arm} summary.`);
    closeToLogged(summary.Median, median, `${key} ${arm} median`);
    closeToLogged(summary.P95, quantile(sorted, 0.95), `${key} ${arm} p95`);
    closeToLogged(summary.MedianAbsoluteDeviation, quantile(deviations, 0.5), `${key} ${arm} MAD`);
  }
  if (
    !Array.isArray(evidence.PairedLogRatios) ||
    evidence.PairedLogRatios.length !== SAMPLE_COUNT
  ) {
    throw new Error(`IntMap ${key}: missing paired log ratios.`);
  }
  logs.forEach((value, index) =>
    closeToLogged(evidence.PairedLogRatios[index], value, `${key} paired log ratio ${index}`)
  );
  let state = (evidence.Seed >>> 0) | 1;
  const bootstrap = [];
  const batches = SAMPLE_COUNT / BATCH_SIZE;
  for (let repetition = 0; repetition < BOOTSTRAP_REPETITIONS; repetition++) {
    let sum = 0;
    for (let batch = 0; batch < batches; batch++) {
      state ^= state << 13;
      state ^= state >>> 17;
      state ^= state << 5;
      const offset = ((state >>> 0) % batches) * BATCH_SIZE;
      for (let cycle = 0; cycle < BATCH_SIZE; cycle++) sum += logs[offset + cycle];
    }
    bootstrap.push(Math.exp(sum / SAMPLE_COUNT));
  }
  bootstrap.sort((a, b) => a - b);
  const lower95 = quantile(bootstrap, 0.025);
  const upper95 = quantile(bootstrap, 0.975);
  if (!(evidence.RatioLower95 > 0) || evidence.RatioUpper95 < evidence.RatioLower95) {
    throw new Error(`IntMap ${key}: invalid confidence interval.`);
  }
  closeToLogged(evidence.RatioLower95, lower95, `${key} lower interval`);
  closeToLogged(evidence.RatioUpper95, upper95, `${key} upper interval`);
  return {
    ratio,
    lower95,
    upper95,
    referenceSpread,
    subjectSpread,
    stable: sufficient && referenceSpread <= 0.03 && subjectSpread <= 0.03
  };
}

function verifyAcceptance(root, commit, unityVersion) {
  const log = fs.readFileSync(path.join(root, "intmap", "player.log"), "utf8");
  const workloads = new Map();
  for (const match of log.matchAll(/INTMAP_PAIRED_SAMPLES (1000|10000) (0|50) (\{[^\r\n]+\})/g)) {
    const key = `${match[1]}/${match[2]}`;
    if (workloads.has(key)) throw new Error(`Duplicate IntMap workload ${key}.`);
    const evidence = JSON.parse(match[3]);
    const metadata = evidence.EnvironmentMetadata;
    if (
      !metadata ||
      metadata.commit !== commit ||
      metadata.unityVersion !== unityVersion ||
      metadata.backend !== "IL2CPP" ||
      metadata.isEditor !== "False" ||
      metadata.buildConfiguration !== "Release"
    ) {
      throw new Error(
        `IntMap ${key}: evidence does not identify the requested Release IL2CPP player.`
      );
    }
    for (const arm of ["ReferenceMilliseconds", "SubjectMilliseconds"]) {
      const samples = evidence[arm];
      if (
        !Array.isArray(samples) ||
        samples.length !== 32 ||
        samples.some((sample) => !Number.isFinite(sample) || sample <= 0)
      ) {
        throw new Error(`IntMap ${key}: expected 32 positive finite samples per arm.`);
      }
    }
    workloads.set(key, verifyStatistics(evidence, key));
  }
  if (workloads.size !== 4)
    throw new Error("Expected all four IntMap workloads in the player log.");
  const stable = [...workloads.values()].every((workload) => workload.stable);
  const hitWin = ["1000/0", "10000/0"].every((key) => {
    const workload = workloads.get(key);
    return workload.ratio >= 1.3 && workload.lower95 > 1;
  });
  return {
    decision: !stable ? "inconclusive" : hitWin ? "meets-hit-margin" : "below-hit-margin",
    workloads: Object.fromEntries(workloads)
  };
}

if (require.main === module) {
  try {
    fs.rmSync(path.join(process.argv[2], "acceptance-summary.json"), { force: true });
    const result = verifyAcceptance(...process.argv.slice(2));
    const output = JSON.stringify(result, null, 2);
    fs.writeFileSync(path.join(process.argv[2], "acceptance-summary.json"), `${output}\n`);
    process.stdout.write(`${output}\n`);
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = { verifyAcceptance };
