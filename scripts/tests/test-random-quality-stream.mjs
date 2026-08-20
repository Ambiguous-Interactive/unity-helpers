// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const project = path.join(
  repoRoot,
  "Generator~",
  "WallstopStudios.UnityHelpers.RandomQuality",
  "WallstopStudios.UnityHelpers.RandomQuality.csproj"
);
const seed = "00010203-0405-0607-0809-0a0b0c0d0e0f";
const built = spawnSync("dotnet", ["build", project, "-c", "Release", "--nologo", "-v", "quiet"], {
  cwd: repoRoot,
  encoding: null,
  maxBuffer: 4 * 1024 * 1024
});
assert.equal(built.status, 0, built.stderr.toString());
const host = path.join(
  repoRoot,
  "Generator~",
  "WallstopStudios.UnityHelpers.RandomQuality",
  "bin",
  "Release",
  "net9.0",
  "WallstopStudios.UnityHelpers.RandomQuality.dll"
);

function run(...args) {
  return spawnSync("dotnet", [host, ...args], {
    cwd: repoRoot,
    encoding: null,
    maxBuffer: 4 * 1024 * 1024
  });
}

const listed = run("--list");
assert.equal(listed.status, 0, listed.stderr.toString());
const names = listed.stdout.toString().trim().split(/\r?\n/);
const expectedNames = [
  "BlastCircuitRandom",
  "DotNetRandom",
  "FlurryBurstRandom",
  "IllusionFlow",
  "LinearCongruentialGenerator",
  "PcgRandom",
  "PhotonSpinRandom",
  "RomuDuo",
  "SplitMix64",
  "SquirrelRandom",
  "StormDropRandom",
  "SystemRandom",
  "WaveSplatRandom",
  "WDoomRandom",
  "WyRandom",
  "XoroShiroRandom",
  "XorShiftRandom"
];
assert.deepEqual(names, expectedNames, "the public standalone inventory must not drift silently");

for (const name of names) {
  const firstShort = run("--generator", name, "--seed", seed, "--bytes", "37");
  const secondShort = run("--generator", name, "--seed", seed, "--bytes", "37");
  assert.equal(firstShort.status, 0, `${name}: ${firstShort.stderr.toString()}`);
  assert.equal(secondShort.status, 0, `${name}: ${secondShort.stderr.toString()}`);
  assert.equal(firstShort.stderr.length, 0, `${name} wrote diagnostics on success`);
  assert.equal(firstShort.stdout.length, 37, `${name} did not honor an unaligned byte count`);
  assert.deepEqual(firstShort.stdout, secondShort.stdout, `${name} ignored its deterministic seed`);
}

const first = run("--generator", "PcgRandom", "--seed", seed, "--bytes", "257");
const second = run("--generator", "PcgRandom", "--seed", seed, "--bytes", "257");
assert.equal(first.status, 0, first.stderr.toString());
assert.equal(second.status, 0, second.stderr.toString());
assert.equal(first.stderr.length, 0);
assert.equal(second.stderr.length, 0);
assert.equal(first.stdout.length, 257);
assert.deepEqual(first.stdout, second.stdout, "a seed must reproduce the exact byte stream");
assert.deepEqual(
  first.stdout.subarray(0, 8),
  Buffer.from("98b0e0c0ca3eccd3", "hex"),
  "the pinned PCG vector must remain little-endian"
);

const other = run(
  "--generator",
  "PcgRandom",
  "--seed",
  "10111213-1415-1617-1819-1a1b1c1d1e1f",
  "--bytes",
  "257"
);
assert.equal(other.status, 0, other.stderr.toString());
assert.notDeepEqual(first.stdout, other.stdout, "different seeds must select different streams");

const invalid = run("--generator", "NotAGenerator", "--bytes", "4");
assert.equal(invalid.status, 2);
assert.equal(invalid.stdout.length, 0, "diagnostics must never contaminate binary stdout");
