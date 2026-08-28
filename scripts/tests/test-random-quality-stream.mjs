// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
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
  "Sfc64Random",
  "SplitMix64",
  "SquirrelRandom",
  "StormDropRandom",
  "SystemRandom",
  "WaveSplatRandom",
  "WDoomRandom",
  "WyRandom",
  "XoroShiroRandom",
  "XorShiftRandom",
  "Xoshiro128StarStar",
  "Xoshiro256StarStar"
];
assert.deepEqual(names, expectedNames, "the public standalone inventory must not drift silently");

const hotPathSources = [...expectedNames, "NativePcgRandom", "UnityRandom"].sort();
const randomSourceRoot = path.join(repoRoot, "Runtime", "Core", "Random");
const discoveredSources = fs
  .readdirSync(randomSourceRoot)
  .filter((file) => file.endsWith(".cs"))
  .filter((file) => {
    const source = fs.readFileSync(path.join(randomSourceRoot, file), "utf8");
    return source.includes("[RandomGeneratorMetadata(") || file === "NativePcgRandom.cs";
  })
  .map((file) => path.basename(file, ".cs"))
  .sort();
assert.deepEqual(
  discoveredSources,
  hotPathSources,
  "every concrete random generator must participate in the hot-path repair audit"
);

function methodBody(source, openBrace) {
  let depth = 0;
  for (let index = openBrace; index < source.length; index += 1) {
    if (source[index] === "{") {
      depth += 1;
    } else if (source[index] === "}") {
      depth -= 1;
      if (depth === 0) {
        return source.slice(openBrace + 1, index);
      }
    }
  }
  throw new Error("unterminated random draw method");
}

for (const generator of hotPathSources) {
  const source = fs.readFileSync(path.join(randomSourceRoot, `${generator}.cs`), "utf8");
  const signature =
    /\b(?:public|protected|private|internal)\s+(?:(?:static|virtual|override|unsafe|readonly|new)\s+)*[\w<>,.?\[\]]+\s+(Next\w*)\s*\([^;{}]*\)\s*\{/g;
  let methods = 0;
  for (const match of source.matchAll(signature)) {
    methods += 1;
    const openBrace = match.index + match[0].lastIndexOf("{");
    const body = methodBody(source, openBrace);
    assert.doesNotMatch(
      body,
      /\b(?:Ensure|Normalize|Repair|Initialize)\w*\s*\(/,
      `${generator}.${match[1]} moved state repair back into a draw path`
    );
  }
  assert.ok(methods > 0, `${generator} exposes no audited draw method`);
}

const abstractSource = fs.readFileSync(path.join(randomSourceRoot, "AbstractRandom.cs"), "utf8");
const abstractDrawSignature =
  /\bpublic\s+(?:(?:static|virtual|override|unsafe|readonly|new)\s+)*[\w<>,.?\[\]]+\s+(Next\w*)\s*\([^;{}]*\)\s*\{/g;
let abstractDrawMethods = 0;
for (const match of abstractSource.matchAll(abstractDrawSignature)) {
  abstractDrawMethods += 1;
  const openBrace = match.index + match[0].lastIndexOf("{");
  assert.doesNotMatch(
    methodBody(abstractSource, openBrace),
    /\b(?:Repair|Normalize|Initialize)\w*\s*\(/,
    `AbstractRandom.${match[1]} moved state repair into a shared draw path`
  );
}
assert.ok(abstractDrawMethods > 0, "AbstractRandom exposes no audited shared draw methods");

const guidScratch = abstractSource.match(/private byte\[\] GenerateGuidBytes\(\)\s*\{/);
assert.ok(guidScratch, "AbstractRandom.GenerateGuidBytes must remain discoverable");
const guidScratchBody = methodBody(
  abstractSource,
  guidScratch.index + guidScratch[0].lastIndexOf("{")
);
assert.doesNotMatch(
  guidScratchBody,
  /\b(?:if\s*\(|new byte\s*\[|RepairCommonState\s*\()/,
  "GUID scratch repair belongs outside the draw path"
);
assert.match(
  abstractSource,
  /private void OnProtoDeserialize\(\)\s*\{\s*RepairCommonState\(\);/,
  "the shared root callback must repair non-constructor instances"
);

const photonSource = fs.readFileSync(path.join(randomSourceRoot, "PhotonSpinRandom.cs"), "utf8");
const photonNext = photonSource.match(/public override uint NextUint\(\)\s*\{([\s\S]*?)\n\s*\}/);
assert.ok(photonNext, "PhotonSpinRandom.NextUint must remain discoverable");
assert.doesNotMatch(
  photonNext[1],
  /_hasPrimed/,
  "PhotonSpin priming belongs outside the draw path"
);

const stormSource = fs.readFileSync(path.join(randomSourceRoot, "StormDropRandom.cs"), "utf8");
const stormNext = stormSource.match(/public override uint NextUint\(\)\s*\{([\s\S]*?)\n\s*\}/);
assert.ok(stormNext, "StormDropRandom.NextUint must remain discoverable");
assert.doesNotMatch(stormNext[1], /\bif\s*\(/, "StormDrop's draw path must stay branchless");

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

const photon = run(
  "--generator",
  "PhotonSpinRandom",
  "--seed",
  "12345678-1234-1234-1234-123456789012",
  "--bytes",
  "64"
);
assert.equal(photon.status, 0, photon.stderr.toString());
assert.deepEqual(
  photon.stdout,
  Buffer.from(
    "28e54e19a64cf2b2506af201132481cd02efe058c0930ad31200a2afba564f573" +
      "ea9b29585deae825d66a48f515207d13e97b231b96534e39ed4b986bda3b097",
    "hex"
  ),
  "eager PhotonSpin priming must preserve the published first-draw stream"
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
