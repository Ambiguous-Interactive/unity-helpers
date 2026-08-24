#!/usr/bin/env node
/**
 * The two analyzer DLLs this package ships are committed under `Runtime/Analyzers`, and CI compares
 * them byte-for-byte against a fresh `dotnet build -c Release` of the sources beside them.
 *
 * Nothing local did. Editing an analyzer source and forgetting the rebuild was therefore a ~10
 * minute round trip through `WallstopProto Generator` to discover, and CSharpier reformatting the
 * source is enough to trigger it -- which is exactly how session 221 hit it, from a mechanical
 * comparison-direction sweep that touched one analyzer file.
 *
 * Each project's Release build copies its own output into `Runtime/Analyzers`, so "is the shipped
 * DLL stale?" is answerable without a second copy: build, then ask git whether the tracked DLL
 * moved. A rebuild is idempotent when the DLL was already current, which is what makes this safe to
 * run unconditionally.
 *
 * Exit codes: 0 = shipped DLLs match their sources (or were refreshed by --fix), 1 = stale.
 */

"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..");

const PROJECTS = [
  "Generator~/WallstopStudios.UnityHelpers.Analyzers/WallstopStudios.UnityHelpers.Analyzers.csproj",
  "Generator~/WallstopStudios.UnityHelpers.Proto.Generator/WallstopStudios.UnityHelpers.Proto.Generator.csproj"
];
const SHIPPED = "Runtime/Analyzers";

function git(args) {
  return spawnSync("git", ["-C", REPO_ROOT, ...args], { encoding: "utf8" });
}

/**
 * Content hashes of the shipped assemblies, keyed by repo-relative path.
 *
 * NOT `git status --porcelain`: porcelain records THAT a path is modified, never which bytes it
 * holds. A DLL that was already dirty and is rewritten with different content still reads ` M path`
 * both times, so a before/after comparison of that string returns "unchanged" and the check passes
 * on a stale assembly (Bugbot, PR #555).
 */
function shippedHashes() {
  const directory = path.join(REPO_ROOT, SHIPPED);
  const hashes = new Map();
  if (!fs.existsSync(directory)) {
    return hashes;
  }
  for (const entry of fs.readdirSync(directory)) {
    if (!entry.endsWith(".dll")) {
      continue;
    }
    const full = path.join(directory, entry);
    hashes.set(
      `${SHIPPED}/${entry}`,
      crypto.createHash("sha256").update(fs.readFileSync(full)).digest("hex")
    );
  }
  return hashes;
}

function main(argv) {
  const fix = argv.includes("--fix");

  const before = shippedHashes();

  for (const project of PROJECTS) {
    const build = spawnSync(
      "dotnet",
      ["build", path.join(REPO_ROOT, project), "-c", "Release", "--nologo", "-v", "quiet"],
      { encoding: "utf8", cwd: REPO_ROOT }
    );
    if (build.status !== 0) {
      console.error(`[shipped-analyzers] ${project} failed to build:`);
      console.error(build.stdout || build.stderr);
      return 1;
    }
  }

  const after = shippedHashes();
  const moved = [...after.keys()].filter((file) => before.get(file) !== after.get(file));
  if (moved.length === 0) {
    return 0;
  }
  if (fix) {
    console.log(
      `[shipped-analyzers] Rebuilt ${moved.length} shipped analyzer file(s); stage them with the source change:`
    );
    for (const file of moved) {
      console.log(`  ${file}`);
    }
    return 0;
  }

  console.error(
    "[shipped-analyzers] The shipped analyzer assemblies do not match the sources beside them."
  );
  for (const file of moved) {
    console.error(`  ${file}`);
  }
  console.error("  Fix: npm run verify:shipped-analyzers:fix, then stage the rebuilt DLL(s).");
  console.error(
    "  CI compares these byte-for-byte, so a stale one fails 'WallstopProto Generator'."
  );
  return 1;
}

module.exports = { PROJECTS, SHIPPED };

if (require.main === module) {
  process.exit(main(process.argv.slice(2)));
}
