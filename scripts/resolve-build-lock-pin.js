"use strict";

// Resolves the commit an Ambiguous-Interactive build-lock action is pinned to, by reading the
// `uses:` lines in `.github/` rather than restating the SHA anywhere. Dependabot bumps those
// lines and cannot bump a hand-copied literal, so every copy drifts on the next bump -- loudly
// in a contract test, and silently wherever the copy selects which policy version to check out.
// scripts/tests/test-unity-workflow-matrix-contract.ps1 enforces the same invariants for
// PowerShell callers.

const fs = require("node:fs");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..");
const actionPathPrefix = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/";

function collectWorkflowFiles(directory) {
  const found = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      found.push(...collectWorkflowFiles(entryPath));
    } else if (entry.name.endsWith(".yml") || entry.name.endsWith(".yaml")) {
      found.push(entryPath);
    }
  }
  return found.sort();
}

/**
 * @param {string} actionName Build-lock action directory name, e.g. "require-confirmed-unity-cleanup".
 * @param {string} root Repository root to scan. Defaults to this repository.
 * @returns {string} The single 40-character commit SHA every usage pins.
 */
function resolveBuildLockPin(actionName, root = repoRoot) {
  const githubRoot = path.join(root, ".github");
  if (!fs.existsSync(githubRoot)) {
    throw new Error(`No .github directory under ${root}`);
  }

  // Anchored to a live `uses:` line: matching the action path anywhere would let a commented-out
  // reference select the commit this resolver hands to `ref:`, checking out the wrong policy.
  const pattern = new RegExp(
    `^[ \\t]*(?:-[ \\t]*)?uses:[ \\t]*${actionPathPrefix.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}${actionName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}@(\\S+)`,
    "gm"
  );

  const usages = [];
  for (const file of collectWorkflowFiles(githubRoot)) {
    const content = fs.readFileSync(file, "utf8");
    for (const match of content.matchAll(pattern)) {
      usages.push({
        reference: match[1],
        file: path.relative(root, file).split(path.sep).join("/")
      });
    }
  }

  if (usages.length === 0) {
    throw new Error(
      `No workflow or composite action references the build-lock action '${actionName}'`
    );
  }

  const unpinned = usages.filter((usage) => !/^[0-9a-f]{40}$/.test(usage.reference));
  if (unpinned.length > 0) {
    const detail = unpinned.map((usage) => `${usage.file} -> @${usage.reference}`).join(", ");
    throw new Error(
      `Build-lock action '${actionName}' must be pinned to a full 40-character commit SHA, never a tag or branch. Offending: ${detail}`
    );
  }

  const distinct = [...new Set(usages.map((usage) => usage.reference))];
  if (distinct.length !== 1) {
    const detail = usages.map((usage) => `${usage.file} -> @${usage.reference}`).join(", ");
    throw new Error(
      `Build-lock action '${actionName}' is pinned to ${distinct.length} different commits. Usages: ${detail}`
    );
  }

  return distinct[0];
}

module.exports = { resolveBuildLockPin };

if (require.main === module) {
  const actionName = process.argv[2];
  if (!actionName) {
    console.error("usage: node scripts/resolve-build-lock-pin.js <build-lock-action-name>");
    process.exit(2);
  }

  try {
    process.stdout.write(`${resolveBuildLockPin(actionName)}\n`);
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
