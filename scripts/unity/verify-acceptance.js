#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const path = require("node:path");

const { verifyStatistics } = require("./lib/calibrated-statistics");

function verifyAcceptance(root, commit, unityVersion) {
  const log = fs.readFileSync(path.join(root, "intmap", "player.log"), "utf8");
  const workloads = new Map();
  for (const match of log.matchAll(/INTMAP_PAIRED_SAMPLES (1000|10000) (0|50) (\{[^\r\n]+\})/g)) {
    const key = `${match[1]}/${match[2]}`;
    if (workloads.has(key)) throw new Error(`Duplicate IntMap workload ${key}.`);
    const evidence = JSON.parse(match[3]);
    if (Object.hasOwn(evidence, "SlotDiagnostics")) {
      throw new Error(`IntMap ${key}: diagnostic observations cannot establish acceptance.`);
    }
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
