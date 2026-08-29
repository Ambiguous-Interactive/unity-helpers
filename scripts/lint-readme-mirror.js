#!/usr/bin/env node
/**
 * Fails when `docs/readme.md` is not what `README.md` generates.
 *
 * The same wrapper shape as `lint-doc-counts.ps1` over `sync-doc-counts.ps1`: the generator owns
 * the rewrite rules and the check is the generator refusing to write. One definition, two entry
 * points, so a rule can never be enforced in a form the fix does not produce.
 *
 * `--sync-script <path>` points at a different generator. Nothing in CI passes it; it exists so
 * the self-test can drive BOTH of this wrapper's own rules -- the missing-generator guard and the
 * exit-code propagation -- without a tree that is clean by construction (#556).
 *
 * Exit codes: whatever the generator's `--check` returned, or 1 when it could not be run.
 */

"use strict";

const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

/** @returns {number} Process exit code. */
function main(argv) {
  const scriptFlag = argv.indexOf("--sync-script");
  if (0 <= scriptFlag && argv.length <= scriptFlag + 1) {
    console.error("[readme-mirror] --sync-script requires a path.");
    return 1;
  }
  const syncScript =
    0 <= scriptFlag
      ? path.resolve(REPO_ROOT, argv[scriptFlag + 1])
      : path.join(__dirname, "sync-readme-mirror.js");

  if (!fs.existsSync(syncScript)) {
    console.error(`[readme-mirror] generator not found at: ${syncScript}`);
    return 1;
  }

  // Guarded on `0 <= scriptFlag`: with the flag absent it is -1, and `index !== scriptFlag + 1`
  // would then swallow argv[0] -- which ate `--verbose` on the first run of this file.
  const passthrough =
    0 <= scriptFlag
      ? argv.filter((argument, index) => index !== scriptFlag && index !== scriptFlag + 1)
      : argv;
  const result = spawnSync(process.execPath, [syncScript, "--check", ...passthrough], {
    cwd: REPO_ROOT,
    stdio: "inherit",
    windowsHide: true
  });
  if (result.error) {
    console.error(`[readme-mirror] failed to launch the generator: ${result.error.message}`);
    return 1;
  }
  return result.status === null ? 1 : result.status;
}

module.exports = { main };

if (require.main === module) {
  process.exit(main(process.argv.slice(2)));
}
