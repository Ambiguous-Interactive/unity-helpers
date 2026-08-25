#!/usr/bin/env node
/**
 * The two analyzer DLLs this package ships are committed under `Runtime/Analyzers`, and the
 * `WallstopProto Generator` workflow compares them byte-for-byte against a fresh
 * `dotnet build -c Release` of the sources beside them.
 *
 * Nothing local did. Editing an analyzer source and forgetting the rebuild was therefore a ~10
 * minute round trip through that workflow to discover, and CSharpier reformatting the source is
 * enough to trigger it -- which is exactly how session 221 hit it, from a mechanical
 * comparison-direction sweep that touched one analyzer file.
 *
 * This runs the workflow's comparison, not an approximation of it: each project is built with
 * `AnalyzerPayloadOutputDir` redirected at a scratch directory, and the bytes it produces are
 * compared against the committed ones. An earlier version instead built INTO the tree and diffed
 * the directory against itself before and after, which could not fail in three separate ways
 * (#558): a renamed `Runtime/Analyzers` made both sides an empty map, a deleted DLL contributed no
 * key to the `after` side it was filtered from, and a `.csproj` edit that stopped the copy made the
 * two sides identical by construction -- the exact scenario the gate exists for, inverted.
 *
 * Exit codes: 0 = shipped DLLs match their sources (or were refreshed by --fix), 1 = stale.
 */

"use strict";

const crypto = require("crypto");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..");

/**
 * Every DLL the package ships as a compiler input, named rather than discovered.
 *
 * Discovering them by reading the directory is what let an empty or renamed `Runtime/Analyzers`
 * report success: "found nothing" and "there is nothing to find" are the same empty map. Naming
 * them means their absence is a failure with a name in it.
 */
const ANALYZERS = [
  {
    project:
      "Generator~/WallstopStudios.UnityHelpers.Analyzers/WallstopStudios.UnityHelpers.Analyzers.csproj",
    assembly: "WallstopStudios.UnityHelpers.Analyzers.dll"
  },
  {
    project:
      "Generator~/WallstopStudios.UnityHelpers.Proto.Generator/WallstopStudios.UnityHelpers.Proto.Generator.csproj",
    assembly: "WallstopStudios.UnityHelpers.Proto.Generator.dll"
  }
];
const SHIPPED = "Runtime/Analyzers";
/** Kept for the placement contract test, which enumerates the same shipped directory. */
const PROJECTS = ANALYZERS.map((analyzer) => analyzer.project);

function sha256(file) {
  return crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
}

/** Content hashes of every `.dll` directly inside `directory`, keyed by bare file name. */
function dllHashes(directory) {
  const hashes = new Map();
  if (!fs.existsSync(directory)) {
    return hashes;
  }
  for (const entry of fs.readdirSync(directory)) {
    if (entry.endsWith(".dll")) {
      hashes.set(entry, sha256(path.join(directory, entry)));
    }
  }
  return hashes;
}

/** Builds one analyzer project with its payload redirected away from the tree being verified. */
function dotnetBuild({ project, outputDirectory }) {
  const build = spawnSync(
    "dotnet",
    [
      "build",
      path.join(REPO_ROOT, project),
      "-c",
      "Release",
      "--nologo",
      "-v",
      "quiet",
      `-p:AnalyzerPayloadOutputDir=${outputDirectory}`
    ],
    { encoding: "utf8", cwd: REPO_ROOT }
  );
  return { ok: build.status === 0, output: build.stdout || build.stderr || "" };
}

/**
 * @param options.shippedDirectory absolute path holding the committed DLLs
 * @param options.scratchDirectory absolute path a build may write its payload into
 * @param options.build            `({project, outputDirectory}) => {ok, output}`
 */
function verify(options) {
  const {
    shippedDirectory,
    scratchDirectory,
    build = dotnetBuild,
    fix = false,
    log = console.log,
    logError = console.error
  } = options;

  if (!fs.existsSync(shippedDirectory)) {
    logError(
      `[shipped-analyzers] The shipped analyzer directory ${shippedDirectory} does not exist.`
    );
    logError(
      "  This check compares the committed analyzer DLLs against a fresh build; with the directory"
    );
    logError("  gone there is nothing to compare and the comparison is not silently skipped.");
    return 1;
  }

  fs.mkdirSync(scratchDirectory, { recursive: true });
  for (const { project } of ANALYZERS) {
    const result = build({ project, outputDirectory: scratchDirectory });
    if (!result.ok) {
      logError(`[shipped-analyzers] ${project} failed to build:`);
      logError(result.output);
      return 1;
    }
  }

  const fresh = dllHashes(scratchDirectory);
  const shipped = dllHashes(shippedDirectory);

  const notBuilt = ANALYZERS.filter(({ assembly }) => !fresh.has(assembly));
  if (0 < notBuilt.length) {
    logError(
      "[shipped-analyzers] The Release build did not produce every analyzer this package ships."
    );
    for (const { assembly, project } of notBuilt) {
      logError(`  ${assembly} (expected from ${project})`);
    }
    logError(
      "  Nothing was compared. Check that the project still sets AnalyzerPayloadOutputDir and"
    );
    logError("  copies its output there.");
    return 1;
  }

  // The union, deliberately. Iterating either side alone hides the other side's absences: a DLL
  // that the build stopped producing, or one deleted from the tree, contributes no key to the side
  // it is missing from and so can never appear in a difference computed from that side.
  const names = [...new Set([...fresh.keys(), ...shipped.keys()])].sort();
  const missing = [];
  const stale = [];
  const unexpected = [];
  for (const name of names) {
    if (!fresh.has(name)) {
      unexpected.push(name);
    } else if (!shipped.has(name)) {
      missing.push(name);
    } else if (fresh.get(name) !== shipped.get(name)) {
      stale.push(name);
    }
  }

  if (0 < unexpected.length) {
    logError(
      `[shipped-analyzers] ${SHIPPED} holds ${unexpected.length} DLL(s) no analyzer project produces.`
    );
    for (const name of unexpected) {
      logError(`  ${SHIPPED}/${name}`);
    }
    logError(
      "  Unity loads every DLL here as a compiler input, so an unlisted one ships ungoverned."
    );
    logError(`  Add its project to ANALYZERS in ${path.relative(REPO_ROOT, __filename)}, or`);
    logError("  delete the DLL.");
    return 1;
  }

  if (missing.length === 0 && stale.length === 0) {
    return 0;
  }

  if (fix) {
    for (const name of [...missing, ...stale]) {
      fs.copyFileSync(path.join(scratchDirectory, name), path.join(shippedDirectory, name));
    }
    log(
      `[shipped-analyzers] Refreshed ${missing.length + stale.length} shipped analyzer file(s); stage them with the source change:`
    );
    for (const name of [...missing, ...stale].sort()) {
      log(`  ${SHIPPED}/${name}`);
    }
    return 0;
  }

  logError(
    "[shipped-analyzers] The shipped analyzer assemblies do not match the sources beside them."
  );
  for (const name of missing) {
    logError(`  ${SHIPPED}/${name} is missing; the Release build produces it.`);
  }
  for (const name of stale) {
    logError(`  ${SHIPPED}/${name} differs from a fresh Release build of its sources.`);
  }
  logError("  Fix: npm run verify:shipped-analyzers:fix, then stage the rebuilt DLL(s).");
  logError("  CI compares these byte-for-byte, so a stale one fails 'WallstopProto Generator'.");
  return 1;
}

function main(argv) {
  const scratchDirectory = fs.mkdtempSync(path.join(os.tmpdir(), "shipped-analyzers-"));
  try {
    return verify({
      shippedDirectory: path.join(REPO_ROOT, SHIPPED),
      scratchDirectory,
      fix: argv.includes("--fix")
    });
  } finally {
    fs.rmSync(scratchDirectory, { recursive: true, force: true });
  }
}

module.exports = { ANALYZERS, PROJECTS, SHIPPED, verify };

if (require.main === module) {
  process.exit(main(process.argv.slice(2)));
}
