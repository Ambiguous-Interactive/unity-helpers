#!/usr/bin/env node
"use strict";

// Self-test for the shipped-analyzer gate (#558).
//
// The gate scans a corpus that is clean by construction, so a green run over the real repository is
// not evidence that it can go red -- which is the whole of #556. Three of its paths could not:
// a renamed `Runtime/Analyzers` made both sides of the comparison an empty map, a deleted DLL
// contributed no key to the side the difference was computed from, and a `.csproj` edit that
// stopped the copy made the two sides identical by construction.
//
// Every case below drives the REAL `verify` with a stub build, and each red half asserts the
// message its OWN guard emits. Asserting only a non-zero exit would let a case pass by tripping a
// different guard than the one it is named for, which is how an unfalsifiable gate looks from the
// outside.
// cspell:ignore msbuild

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const { ANALYZERS, verify } = require("../verify-shipped-analyzers.js");

const REPO_ROOT = path.resolve(__dirname, "../..");

let passed = 0;

/**
 * A build that writes deterministic bytes for each named assembly, so "a fresh build" is
 * reproducible without a compiler. `omit` names assemblies this build stops producing.
 */
function stubBuild(omit = []) {
  return ({ outputDirectory }) => {
    for (const { assembly } of ANALYZERS) {
      if (omit.includes(assembly)) {
        continue;
      }
      fs.writeFileSync(path.join(outputDirectory, assembly), `fresh bytes of ${assembly}`);
    }
    return { ok: true, output: "" };
  };
}

/** A shipped directory holding exactly what `stubBuild` produces. */
function makeCase() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "verify-shipped-analyzers-test-"));
  const shippedDirectory = path.join(root, "Runtime", "Analyzers");
  const scratchDirectory = path.join(root, "scratch");
  fs.mkdirSync(shippedDirectory, { recursive: true });
  for (const { assembly } of ANALYZERS) {
    fs.writeFileSync(path.join(shippedDirectory, assembly), `fresh bytes of ${assembly}`);
  }
  return { root, shippedDirectory, scratchDirectory };
}

function run({ shippedDirectory, scratchDirectory }, { build = stubBuild(), fix = false } = {}) {
  const lines = [];
  const code = verify({
    shippedDirectory,
    scratchDirectory,
    build,
    fix,
    log: (message) => lines.push(String(message)),
    logError: (message) => lines.push(String(message))
  });
  return { code, output: lines.join("\n") };
}

function check(description, condition, detail) {
  assert.ok(condition, `${description}${detail ? `: ${detail}` : ""}`);
  passed += 1;
  console.log(`  [PASS] ${description}`);
}

function withCase(body) {
  const fixture = makeCase();
  try {
    body(fixture);
  } finally {
    fs.rmSync(fixture.root, { recursive: true, force: true });
  }
}

console.log("Section: green half");

withCase((fixture) => {
  const { code, output } = run(fixture);
  check("an unmodified tree passes", code === 0, `exit ${code}: ${output}`);
});

console.log("Section: analyzer build configuration");

function runMsBuild(project, args) {
  return spawnSync("dotnet", ["msbuild", path.join(REPO_ROOT, project), "-nologo", ...args], {
    cwd: REPO_ROOT,
    encoding: "utf8"
  });
}

for (const { project, assembly } of ANALYZERS) {
  const debug = runMsBuild(project, ["-getProperty:CopyAnalyzerPayload", "-p:Configuration=Debug"]);
  check(
    `${project} does not copy a Debug payload by default`,
    debug.status === 0 && debug.stdout.trim() === "false",
    `exit ${debug.status}: ${debug.stdout || debug.stderr}`
  );

  const release = runMsBuild(project, [
    "-getProperty:CopyAnalyzerPayload",
    "-p:Configuration=Release"
  ]);
  check(
    `${project} copies a Release payload by default`,
    release.status === 0 && release.stdout.trim() === "true",
    `exit ${release.status}: ${release.stdout || release.stderr}`
  );

  const disabled = runMsBuild(project, [
    "-getProperty:CopyAnalyzerPayload",
    "-p:Configuration=Release",
    "-p:CopyAnalyzerPayload=false"
  ]);
  check(
    `${project} honors an explicitly disabled copy`,
    disabled.status === 0 && disabled.stdout.trim() === "false",
    `exit ${disabled.status}: ${disabled.stdout || disabled.stderr}`
  );

  const rejectedOutputDirectory = fs.mkdtempSync(path.join(os.tmpdir(), "rejected-analyzer-copy-"));
  try {
    const rejected = runMsBuild(project, [
      "-t:CopyAnalyzerToPackage",
      "-p:Configuration=Debug",
      "-p:CopyAnalyzerPayload=true",
      `-p:AnalyzerPayloadOutputDir=${rejectedOutputDirectory}`
    ]);
    const rejectionOutput = `${rejected.stdout || ""}\n${rejected.stderr || ""}`;
    check(
      `${project} rejects an explicit Debug payload copy before writing`,
      rejected.status !== 0 &&
        rejectionOutput.includes("Analyzer payloads can only be copied from a Release build") &&
        !fs.existsSync(path.join(rejectedOutputDirectory, assembly)),
      `exit ${rejected.status}: ${rejectionOutput}`
    );
  } finally {
    fs.rmSync(rejectedOutputDirectory, { recursive: true, force: true });
  }
}

console.log("Section: red halves");

withCase((fixture) => {
  const target = path.join(fixture.shippedDirectory, ANALYZERS[0].assembly);
  fs.writeFileSync(target, "fresh bytes of WallstopStudios.UnityHelpers.Analyzers.dll ");
  const { code, output } = run(fixture);
  check("a byte-mutated shipped DLL fails", code === 1, output);
  check(
    "  ...through the stale guard, naming the file",
    output.includes(`${ANALYZERS[0].assembly} differs from a fresh Release build`),
    output
  );
});

withCase((fixture) => {
  fs.rmSync(path.join(fixture.shippedDirectory, ANALYZERS[1].assembly));
  const { code, output } = run(fixture);
  check("a deleted shipped DLL fails", code === 1, output);
  check(
    "  ...through the missing guard, naming the file",
    output.includes(`${ANALYZERS[1].assembly} is missing`),
    output
  );
});

withCase((fixture) => {
  const absent = path.join(fixture.root, "Runtime", "Renamed");
  const { code, output } = run({
    shippedDirectory: absent,
    scratchDirectory: fixture.scratchDirectory
  });
  check("a shipped directory that does not exist fails", code === 1, output);
  check(
    "  ...through the missing-directory guard",
    output.includes("does not exist") && output.includes(absent),
    output
  );
});

withCase((fixture) => {
  for (const { assembly } of ANALYZERS) {
    fs.rmSync(path.join(fixture.shippedDirectory, assembly));
  }
  const { code, output } = run(fixture);
  check("an empty shipped directory fails", code === 1, output);
  check(
    "  ...through the missing guard, naming every analyzer",
    ANALYZERS.every(({ assembly }) => output.includes(`${assembly} is missing`)),
    output
  );
});

withCase((fixture) => {
  const { code, output } = run(fixture, { build: stubBuild([ANALYZERS[0].assembly]) });
  check("a build that stops producing an analyzer fails", code === 1, output);
  check(
    "  ...through the not-built guard rather than comparing nothing",
    output.includes("did not produce every analyzer") && output.includes(ANALYZERS[0].assembly),
    output
  );
});

withCase((fixture) => {
  fs.writeFileSync(path.join(fixture.shippedDirectory, "Someone.Elses.dll"), "unlisted");
  const { code, output } = run(fixture);
  check("a DLL no analyzer project produces fails", code === 1, output);
  check(
    "  ...through the unexpected guard, naming the file",
    output.includes("Someone.Elses.dll"),
    output
  );
});

console.log("Section: --fix");

withCase((fixture) => {
  fs.writeFileSync(path.join(fixture.shippedDirectory, ANALYZERS[0].assembly), "stale");
  fs.rmSync(path.join(fixture.shippedDirectory, ANALYZERS[1].assembly));

  const fixRun = run(fixture, { fix: true });
  check("--fix repairs a stale and a missing DLL together", fixRun.code === 0, fixRun.output);
  check(
    "  ...and names both so they can be staged",
    ANALYZERS.every(({ assembly }) => fixRun.output.includes(assembly)),
    fixRun.output
  );

  const after = run(fixture);
  check("  ...leaving a tree the unfixed gate passes", after.code === 0, after.output);
});

withCase((fixture) => {
  const absent = path.join(fixture.root, "Runtime", "Renamed");
  const { code } = run(
    { shippedDirectory: absent, scratchDirectory: fixture.scratchDirectory },
    { fix: true }
  );
  check("--fix does not conjure a missing shipped directory", code === 1, `exit ${code}`);
});

console.log(`\n${passed} passed, 0 failed.`);
