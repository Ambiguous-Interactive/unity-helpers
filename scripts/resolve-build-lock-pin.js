"use strict";
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const repoRoot = path.resolve(__dirname, "..");
function resolveBuildLockPin(actionName, root = repoRoot) {
  const result = spawnSync(
    "uv",
    [
      "run",
      "--quiet",
      "--project",
      path.join(repoRoot, "scripts/python~"),
      "--locked",
      "--no-dev",
      "python",
      path.join(repoRoot, "scripts/build_lock_inputs.py"),
      "--resolve",
      actionName,
      "--root",
      root
    ],
    { cwd: repoRoot, encoding: "utf8" }
  );
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(result.stderr.trim() || "Python pin resolver failed.");
  }
  return result.stdout.trim();
}
module.exports = { resolveBuildLockPin };
if (require.main === module) {
  if (!process.argv[2]) {
    console.error("usage: node scripts/resolve-build-lock-pin.js <build-lock-action-name>");
    process.exit(2);
  }
  try {
    process.stdout.write(`${resolveBuildLockPin(process.argv[2])}\n`);
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
