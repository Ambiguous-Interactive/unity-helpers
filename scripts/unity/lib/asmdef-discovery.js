"use strict";

// cspell:ignore OSX iff Integ cls

/**
 * @file asmdef-discovery.js
 *
 * Shared, deterministic discovery + classification of Unity test asmdef files.
 *
 * Used by:
 *   - .github/actions/compute-unity-assemblies (primary CI consumer)
 *
 * No filesystem mutation. Pure functions only.
 *
 * Exports:
 *   - defaultIncludeAssemblies(repoRoot, options?)
 *
 * The enumeration/classification helpers are module-internal; run this file
 * directly (`node scripts/unity/lib/asmdef-discovery.js`) for a self-test
 * that prints every discovered asmdef with its classification.
 *
 * Default include/exclude rules:
 *   - "core"        => INCLUDED by default.
 *   - "perf"        => EXCLUDED by default. Opt in with { includePerf: true }.
 *   - "integration" => EXCLUDED by default (their DI-container packages are not
 *                      in the test project's manifest.json and would fail to
 *                      compile). Opt in with { includeIntegrations: true }.
 */

const fs = require("fs");
const path = require("path");

/**
 * Recursively enumerate files under `dir`, returning the absolute paths whose
 * dirent satisfies `match(fullPath, dirent)`. Pure read-only walk; missing or
 * unreadable directories yield no entries (never throws on ENOENT). Inlined
 * here so this module has no dependency outside scripts/unity/.
 *
 * @param {string} dir - Absolute directory to walk
 * @param {{ match: (full: string, dirent: import('fs').Dirent) => boolean }} options
 * @returns {string[]} Absolute paths of matching files
 */
function walkFiles(dir, options) {
  const match = options && typeof options.match === "function" ? options.match : () => true;
  const results = [];

  /** @param {string} current */
  function recurse(current) {
    let entries;
    try {
      entries = fs.readdirSync(current, { withFileTypes: true });
    } catch {
      // Missing/unreadable directory: contribute nothing rather than throw.
      return;
    }
    for (const dirent of entries) {
      const full = path.join(current, dirent.name);
      if (dirent.isDirectory()) {
        recurse(full);
      } else if (dirent.isFile() && match(full, dirent)) {
        results.push(full);
      }
    }
  }

  recurse(dir);
  return results;
}

/**
 * Names matching this pattern are perf/benchmark assemblies and must be
 * excluded from default Unity Test Runner runs. unity-helpers' perf suite is
 * the `*.Tests.Runtime.Performance` assembly; the run-step category filter
 * additionally drops the [Performance]/[Stress] NUnit categories.
 *
 * @type {RegExp}
 */
const PERF_NAME_REGEX = /(?:Performance)/;

/**
 * Names matching this pattern are DI-container integration suites
 * (VContainer / Zenject / Reflex). EXCLUDED from the default suite because
 * their backing packages (com.gustavopsantos.reflex, com.svermeulen.extenject,
 * jp.hadashikick.vcontainer) are not declared in the test project's
 * manifest.json -- including them would cause compile errors. Opt in via the
 * `includeIntegrations` option on `defaultIncludeAssemblies`.
 *
 * @type {RegExp}
 */
const INTEGRATION_NAME_REGEX = /(?:VContainer|Zenject|Reflex)/;

/**
 * Assembly-name prefix that marks an asmdef as owned by unity-helpers. The
 * Unity Test Runner is invoked with an explicit `-assemblyNames` list, so a
 * foreign test asmdef that happens to live under `Tests/` (for example one
 * pulled in by an external package, or a stray sample) must never be added to
 * the list -- it would not compile against the harness manifest and would fail
 * the run for a reason unrelated to unity-helpers. Every real unity-helpers
 * test assembly is named `WallstopStudios.UnityHelpers.Tests*`, so this owner
 * prefix is a safe, future-proof gate that is a no-op for the current asmdef
 * set.
 *
 * @type {string}
 */
const UNITY_HELPERS_ASSEMBLY_PREFIX = "WallstopStudios.UnityHelpers.";
const STANDALONE_PLATFORM_NAMES = new Set([
  "Standalone",
  "WindowsStandalone32",
  "WindowsStandalone64",
  "LinuxStandalone64",
  "OSXStandalone"
]);

/**
 * True when `name` is a unity-helpers-owned assembly (see
 * {@link UNITY_HELPERS_ASSEMBLY_PREFIX}). Non-string / empty input is treated
 * as NOT owned so a malformed asmdef can never slip through the include gate.
 *
 * @param {string} name - Asmdef assembly name (no extension)
 * @returns {boolean} True iff the name carries the unity-helpers owner prefix
 */
function isUnityHelpersOwnedAssembly(name) {
  return typeof name === "string" && name.startsWith(UNITY_HELPERS_ASSEMBLY_PREFIX);
}

/**
 * Strip the `.asmdef` extension and return the asmdef's declared name. The
 * file's `name` field is the canonical assembly name and must match the
 * filename per Unity convention; we read the JSON to be safe.
 *
 * @param {string} asmdefPath - Absolute path to an .asmdef file
 * @returns {string} Asmdef name (without extension)
 */
function readAsmdefName(asmdefPath) {
  const raw = fs.readFileSync(asmdefPath, "utf8");
  const parsed = JSON.parse(raw);
  if (typeof parsed.name !== "string" || parsed.name.length === 0) {
    // Fall back to the filename to keep this function pure-ish.
    return path.basename(asmdefPath, ".asmdef");
  }
  return parsed.name;
}

/**
 * Classify an asmdef name into a single category.
 *
 * Categories:
 *   - "perf"        -- *.Tests.Runtime.Performance (excluded from PR gates).
 *   - "integration" -- VContainer/Zenject/Reflex DI integration suites.
 *   - "core"        -- Everything else (Editor, Runtime, etc.).
 *
 * @param {string} name - Asmdef assembly name (no extension)
 * @returns {"perf" | "integration" | "core"} Classification
 */
function classifyAsmdef(name) {
  if (typeof name !== "string" || name.length === 0) {
    return "core";
  }

  if (PERF_NAME_REGEX.test(name)) {
    return "perf";
  }

  if (INTEGRATION_NAME_REGEX.test(name)) {
    return "integration";
  }

  return "core";
}

/**
 * @typedef {object} AsmdefEntry
 * @property {string} name - Asmdef assembly name
 * @property {string} path - Absolute path to the asmdef file
 * @property {boolean} isPerf - True when classification is "perf"
 * @property {boolean} isInteg - True when classification is "integration"
 * @property {boolean} isEditorOnly - True iff includePlatforms is exactly ["Editor"]
 * @property {boolean} isForeign - True when the assembly is NOT unity-helpers-owned
 *                     (name lacks the `WallstopStudios.UnityHelpers.` prefix). Such
 *                     assemblies are never added to the Unity `-assemblyNames` list.
 */

/**
 * Read an asmdef's `includePlatforms` array and decide whether the assembly is
 * editor-only. An assembly is editor-only iff `includePlatforms` is exactly
 * `["Editor"]`. Editor-only test assemblies (EditMode suites + Editor
 * integrations) cannot run inside a built player, so the standalone
 * runtime-only flow must exclude them.
 *
 * @param {string} asmdefPath - Absolute path to an .asmdef file
 * @returns {{ includePlatforms: string[], excludePlatforms: string[] }}
 */
function readAsmdefPlatforms(asmdefPath) {
  const raw = fs.readFileSync(asmdefPath, "utf8");
  const parsed = JSON.parse(raw);
  return {
    includePlatforms: Array.isArray(parsed.includePlatforms) ? parsed.includePlatforms : [],
    excludePlatforms: Array.isArray(parsed.excludePlatforms) ? parsed.excludePlatforms : []
  };
}

/**
 * @param {string[]} includePlatforms
 * @param {string[]} excludePlatforms
 * @param {"editmode" | "playmode" | "standalone"} target
 * @returns {boolean}
 */
function isAsmdefCompatibleWithTarget(includePlatforms, excludePlatforms, target) {
  const includes = new Set(includePlatforms);
  const excludes = new Set(excludePlatforms);

  if (target === "standalone") {
    if (excludes.has("Standalone") || excludes.has("WindowsStandalone64")) {
      return false;
    }
    if (includes.size === 0) {
      return true;
    }
    for (const platform of includes) {
      if (STANDALONE_PLATFORM_NAMES.has(platform)) {
        return true;
      }
    }
    return false;
  }

  if (target === "editmode") {
    if (excludes.has("Editor")) {
      return false;
    }
    return includes.size === 0 || includes.has("Editor");
  }

  if (target === "playmode") {
    if (excludes.has("Editor")) {
      return false;
    }
    return includes.size === 0;
  }

  if (excludes.has("Editor")) {
    return false;
  }
  return includes.size === 0 || includes.has("Editor");
}

/**
 * Enumerate every asmdef under `<repoRoot>/Tests/`. Sorted by `name` for
 * stable downstream output (CI summaries, contract tests).
 *
 * @param {string} repoRoot - Absolute path to the repository root
 * @returns {AsmdefEntry[]} Discovered test asmdefs
 */
function enumerateTestAsmdefs(repoRoot) {
  if (typeof repoRoot !== "string" || repoRoot.length === 0) {
    throw new TypeError("enumerateTestAsmdefs: repoRoot must be a non-empty string");
  }

  const testsDir = path.join(repoRoot, "Tests");
  const asmdefPaths = walkFiles(testsDir, {
    match: (full, dirent) => dirent.name.endsWith(".asmdef")
  });

  /** @type {AsmdefEntry[]} */
  const entries = asmdefPaths.map((asmdefPath) => {
    const name = readAsmdefName(asmdefPath);
    const classification = classifyAsmdef(name);
    const platforms = readAsmdefPlatforms(asmdefPath);
    return {
      name,
      path: asmdefPath,
      isPerf: classification === "perf",
      isInteg: classification === "integration",
      includePlatforms: platforms.includePlatforms,
      excludePlatforms: platforms.excludePlatforms,
      isEditorOnly:
        platforms.includePlatforms.length === 1 && platforms.includePlatforms[0] === "Editor",
      isForeign: !isUnityHelpersOwnedAssembly(name)
    };
  });

  entries.sort((a, b) => a.name.localeCompare(b.name));
  return entries;
}

/**
 * @typedef {object} IncludeOptions
 * @property {boolean} [includePerf=false]         Include "perf" asmdefs.
 * @property {boolean} [includeIntegrations=false] Include "integration" asmdefs.
 * @property {"editmode" | "playmode" | "standalone"} [target=editmode]
 *                     Select assemblies compatible with the Unity test target.
 *                     PlayMode and standalone omit editor-only asmdefs.
 * @property {boolean} [runtimeOnly=false]         Back-compat alias for
 *                     target: "standalone". Applied before the perf/integration
 *                     gating so it composes.
 */

/**
 * Names of test asmdefs included in the default Unity Test Runner suite.
 *
 * By default ONLY "core" asmdefs are returned. Perf and integration suites
 * are opt-in:
 *   - includePerf:         add *.Tests.Runtime.Performance.
 *   - includeIntegrations: add VContainer/Zenject/Reflex (caller must ensure
 *                          the corresponding DI packages are in manifest.json).
 *
 * @param {string} repoRoot - Absolute path to the repository root
 * @param {IncludeOptions} [options] - Opt-in flags (default: all false)
 * @returns {string[]} Sorted asmdef names (no extension)
 */
function defaultIncludeAssemblies(repoRoot, options) {
  const opts = options || {};
  const includePerf = opts.includePerf === true;
  const includeIntegrations = opts.includeIntegrations === true;
  const target = opts.target || (opts.runtimeOnly === true ? "standalone" : "editmode");

  return enumerateTestAsmdefs(repoRoot)
    .filter((entry) => {
      // Foreign (non-unity-helpers-owned) asmdefs are never added to the Unity
      // -assemblyNames list: they would not compile against the harness
      // manifest and would fail the run for a reason unrelated to unity-helpers.
      // Gated first, ahead of every other decision. A no-op for the current
      // asmdef set (all entries are unity-helpers-owned).
      if (entry.isForeign) {
        return false;
      }
      if (!isAsmdefCompatibleWithTarget(entry.includePlatforms, entry.excludePlatforms, target)) {
        return false;
      }
      if (entry.isPerf) {
        return includePerf;
      }
      if (entry.isInteg) {
        return includeIntegrations;
      }
      return true;
    })
    .map((entry) => entry.name);
}

/**
 * Names of test asmdefs excluded from the default Unity Test Runner suite.
 * Mirror of `defaultIncludeAssemblies` -- anything not selected by the include
 * options is returned here. With no options, returns all perf + integration
 * asmdefs.
 *
 * @param {string} repoRoot - Absolute path to the repository root
 * @param {IncludeOptions} [options] - Opt-in flags (default: all false)
 * @returns {string[]} Sorted asmdef names (no extension)
 */
function defaultExcludeAssemblies(repoRoot, options) {
  const opts = options || {};
  const includePerf = opts.includePerf === true;
  const includeIntegrations = opts.includeIntegrations === true;
  const target = opts.target || (opts.runtimeOnly === true ? "standalone" : "editmode");

  return enumerateTestAsmdefs(repoRoot)
    .filter((entry) => {
      // Mirror of defaultIncludeAssemblies. Foreign (non-unity-helpers-owned)
      // asmdefs are never included, so they are always "excluded" here too.
      if (entry.isForeign) {
        return true;
      }
      if (!isAsmdefCompatibleWithTarget(entry.includePlatforms, entry.excludePlatforms, target)) {
        return true;
      }
      if (entry.isPerf) {
        return !includePerf;
      }
      if (entry.isInteg) {
        return !includeIntegrations;
      }
      return false;
    })
    .map((entry) => entry.name);
}

// Only defaultIncludeAssemblies has external consumers
// (compute-unity-assemblies/action.yml). The other helpers are internal; the
// self-test block below uses them directly.
module.exports = {
  defaultIncludeAssemblies
};

if (require.main === module) {
  // Self-test mode: print classified asmdefs for the current repo. This file
  // lives at scripts/unity/lib/, so the repo root is three levels up.
  const repoRoot = path.resolve(__dirname, "..", "..", "..");
  const all = enumerateTestAsmdefs(repoRoot);
  const include = defaultIncludeAssemblies(repoRoot);
  const exclude = defaultExcludeAssemblies(repoRoot);

  process.stdout.write(`repoRoot: ${repoRoot}\n`);
  process.stdout.write(`discovered ${all.length} asmdef(s):\n`);
  for (const entry of all) {
    const cls = entry.isPerf ? "perf" : entry.isInteg ? "integration" : "core";
    process.stdout.write(`  [${cls}] ${entry.name}\n`);
  }
  process.stdout.write(
    `\ndefault include (${include.length}, core only -- pass ` +
      `{ includePerf, includeIntegrations } to opt in):\n`
  );
  for (const name of include) {
    process.stdout.write(`  + ${name}\n`);
  }
  process.stdout.write(`\ndefault exclude (${exclude.length}, perf + integration suites):\n`);
  for (const name of exclude) {
    process.stdout.write(`  - ${name}\n`);
  }

  // Diagnostic: runtime-only include list (used by the standalone player flow,
  // where EditMode/editor-only asmdefs cannot run).
  const runtimeInclude = defaultIncludeAssemblies(repoRoot, { target: "standalone" });
  process.stdout.write(
    `\nruntime-only include (${runtimeInclude.length}, drops editor-only asmdefs):\n`
  );
  for (const name of runtimeInclude) {
    process.stdout.write(`  * ${name}\n`);
  }
}
