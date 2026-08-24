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

function main(argv) {
  const fix = argv.includes("--fix");

  // A DLL already dirty before the build cannot be attributed to it, so say so rather than
  // reporting a stale source the author did not cause.
  // NOT trimmed: porcelain's first two columns are the status pair and the first is a SPACE for an
  // unstaged modification, so trimming the whole output eats one character of every path.
  const before = git(["status", "--porcelain", "--", SHIPPED]).stdout;

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

  const after = git(["status", "--porcelain", "--", SHIPPED]).stdout;
  if (after === before) {
    return 0;
  }

  const moved = after
    .split("\n")
    .filter(Boolean)
    .map((line) => line.slice(3));
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
