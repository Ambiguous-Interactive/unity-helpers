#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Structural guard for the Unity license cache location (#690).
//
// The cache holds license identity by definition: retry-license.sh and validate-license-setup.sh
// both name Unity_lic.ulf and UnityEntitlementLicense.xml inside it, and the container's licensing
// client also writes Unity.Entitlements.Audit.log there. scripts/unity/run-ci-tests.ps1 states the
// rule for the Windows path in its SECURITY block: the activation log lives under RUNNER_TEMP or
// the system temp directory, never under the artifacts path. The Docker path had no equivalent, so
// the .unitypackage export -- whose project directory is deliberately inside .artifacts -- put the
// cache one directory away from the four paths its job uploads.
//
// That was never a live leak: the cache is a sibling of every uploaded path, and
// actions/upload-artifact skips dot-prefixed entries unless include-hidden-files is set. Both of
// those are defaults, not invariants, and the two nearest misses are widening a diagnostics upload
// to the project directory or setting include-hidden-files: true. So the assertion below is not
// "no upload names the cache" -- that was already true while the defect existed. It is the stronger
// one: no directory this repository uploads from, nor any ancestor of one, may CONTAIN the cache.
//
// Uploaded paths are read from the workflows with a small line scanner. This repository installs no
// YAML parser, and scripts/tests/test-unity-artifact-redaction.js already reads the same files the
// same way. The scanner collects every `path:`/`paths:` value in every workflow -- a superset of
// what upload-artifact declares, which only makes the guard stricter -- and it is held honest by
// naming subjects it must find, so a scanner that stops matching fails loudly instead of reporting
// a clean repository.

"use strict";

const assert = require("node:assert/strict");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..", "..");
const workflowDirectory = path.join(repoRoot, ".github", "workflows");
const libraryRelativePath = "scripts/unity/lib/license-cache-dir.sh";
const libraryPath = path.join(repoRoot, libraryRelativePath);
const artifactsRoot = path.join(repoRoot, ".artifacts");

/** Every Unity entry point that has to reach the same answer about where the cache lives. */
const cacheConsumers = [
  "scripts/unity/run-unity-docker.sh",
  "scripts/unity/generate-activation.sh",
  "scripts/unity/retry-license.sh",
  "scripts/unity/validate-license-setup.sh"
];

let passed = 0;
let failed = 0;
const failedTests = [];

function runTest(name, fn) {
  try {
    fn();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (err) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${err.message}`);
    failed++;
    failedTests.push(name);
  }
}

function readWorkflows() {
  return fs
    .readdirSync(workflowDirectory)
    .filter((name) => name.endsWith(".yml") || name.endsWith(".yaml"))
    .sort()
    .map((name) => ({
      name,
      lines: fs.readFileSync(path.join(workflowDirectory, name), "utf8").split(/\r?\n/)
    }));
}

/** Normalize a workflow expression to plain text so a prefix comparison is meaningful. */
function normalizePath(value) {
  return String(value)
    .replace(/\$\{\{[^}]*\}\}/g, "")
    .split("\\")
    .join("/")
    .trim();
}

/**
 * Every `path:` / `paths:` value in one workflow, inline and block forms alike.
 *
 * A superset of what actions/upload-artifact declares, on purpose: a path this repository hands to
 * any action is a path it may publish, and over-collecting can only make the guard below stricter.
 */
function pathValuesIn(lines) {
  const values = [];
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    const header = /^(\s*)(-\s+)?paths?:(.*)$/.exec(line);
    if (!header) {
      continue;
    }
    const keyColumn = header[1].length + (header[2] ? header[2].length : 0);
    const inline = header[3].trim();
    if (inline !== "" && !/^[|>][+-]?\d?$/.test(inline)) {
      values.push(inline);
      continue;
    }
    for (let body = index + 1; body < lines.length; body += 1) {
      const candidate = lines[body];
      if (candidate.trim() === "") {
        continue;
      }
      if (candidate.length - candidate.trimStart().length <= keyColumn) {
        break;
      }
      if (candidate.trim().startsWith("#")) {
        continue;
      }
      values.push(candidate.trim().replace(/^-\s+/, ""));
    }
  }
  return values;
}

/** Every workspace-relative path any workflow hands to an action, deduplicated. */
function uploadedPaths() {
  const found = new Set();
  for (const { lines } of readWorkflows()) {
    for (const value of pathValuesIn(lines)) {
      const normalized = normalizePath(value).replace(/^\.\//, "").replace(/\/+$/, "");
      if (normalized === "" || normalized.startsWith("!") || normalized.startsWith("/")) {
        continue;
      }
      found.add(normalized);
    }
  }
  return [...found].sort();
}

/**
 * Every directory an upload reaches into, including the ancestors a widened upload would reach.
 *
 * The workspace root itself is excluded: an upload of the whole checkout is not what this guards,
 * and .gitignore documents an in-workspace cache as a supported manual override.
 */
function uploadedDirectories() {
  const directories = new Set();
  for (const uploaded of uploadedPaths()) {
    const segments = uploaded.split("/");
    const lastSegment = segments[segments.length - 1];
    if (lastSegment.includes(".") || lastSegment.includes("*")) {
      segments.pop();
    }
    while (0 < segments.length) {
      directories.add(segments.join("/"));
      segments.pop();
    }
  }
  return [...directories].sort();
}

/** The Unity project directories this repository points Unity at, workspace-relative or absolute. */
function unityProjectDirectories() {
  const found = new Map();
  const exportScript = fs.readFileSync(
    path.join(repoRoot, "scripts", "unity", "export-unitypackage.sh"),
    "utf8"
  );
  const exportDefault =
    /PROJECT_DIR="\$\{UNITY_PACKAGE_PROJECT_DIR:-\$\{REPO_ROOT\}\/([^"}]+)\}"/.exec(exportScript);
  if (exportDefault) {
    found.set(exportDefault[1], "scripts/unity/export-unitypackage.sh default");
  }
  for (const { name, lines } of readWorkflows()) {
    for (const line of lines) {
      const flag = /--project-dir\s+"?([^"\s\\]+)"?/.exec(line);
      if (flag) {
        found.set(normalizePath(flag[1]).replace(/^\.\//, ""), `.github/workflows/${name}`);
      }
    }
  }
  return found;
}

/** The devcontainer default, read from the wrapper rather than restated here. */
function devcontainerProjectDirectory() {
  const wrapper = fs.readFileSync(
    path.join(repoRoot, "scripts", "unity", "run-unity-docker.sh"),
    "utf8"
  );
  const match = /UNITY_TEST_PROJECT_DIR="\$\{UNITY_TEST_PROJECT_DIR:-([^}]+)\}"/.exec(wrapper);
  assert.ok(match, "run-unity-docker.sh no longer declares a UNITY_TEST_PROJECT_DIR default");
  return match[1];
}

/** Run the shell derivation exactly as the entry points do. */
function resolveCacheDirectory(projectDirectory, explicit, environment) {
  return spawnSync(
    "bash",
    [
      "-c",
      'set -euo pipefail\nsource "$1"\nresolve_unity_license_cache_dir "$2" "$3" "$4"',
      "license-cache-probe",
      libraryPath,
      projectDirectory,
      repoRoot,
      explicit ?? ""
    ],
    { cwd: repoRoot, encoding: "utf8", env: { ...process.env, ...(environment ?? {}) } }
  );
}

function resolvedCacheDirectory(projectDirectory, environment) {
  const result = resolveCacheDirectory(projectDirectory, "", environment);
  assert.strictEqual(
    result.status,
    0,
    `resolving the cache for ${projectDirectory} failed: ${result.stderr}`
  );
  return result.stdout.trim();
}

function isUnder(candidate, root) {
  return candidate === root || candidate.startsWith(`${root}${path.sep}`);
}

console.log("Testing Unity license cache location...\n");

runTest("every Unity entry point delegates to the one derivation", () => {
  // A mention is not a delegation. Removing the `source` line while leaving the comment that names
  // the library behind is exactly the shape a substring match calls covered, so require the two
  // lines that actually do the work.
  assert.ok(fs.existsSync(libraryPath), `${libraryRelativePath} is missing`);
  const missing = cacheConsumers.filter((consumer) => {
    const body = fs.readFileSync(path.join(repoRoot, consumer), "utf8");
    return (
      !/^\s*(?:\.|source)\s+"\$\{SCRIPT_DIR\}\/lib\/license-cache-dir\.sh"\s*$/m.test(body) ||
      !/resolve_unity_license_cache_dir\s/.test(body)
    );
  });
  assert.deepEqual(
    missing,
    [],
    `these scripts derive the license cache without sourcing and calling ${libraryRelativePath}: ` +
      missing.join(", ")
  );
});

runTest("no script open-codes a second license cache derivation", () => {
  // Two helpers answering one question answer it differently, and the one that drifts next is the
  // one nobody tested. The library is the only place the default may be spelled out.
  const scanned = [];
  const offenders = [];
  const walk = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        walk(full);
        continue;
      }
      if (!/\.(?:sh|ps1|js)$/.test(entry.name)) {
        continue;
      }
      const relative = path.relative(repoRoot, full).split(path.sep).join("/");
      if (relative === libraryRelativePath || relative.startsWith("scripts/tests/")) {
        continue;
      }
      scanned.push(relative);
      if (
        fs.readFileSync(full, "utf8").includes("${UNITY_TEST_PROJECT_DIR}/.unity-license-cache")
      ) {
        offenders.push(relative);
      }
    }
  };
  walk(path.join(repoRoot, "scripts"));
  assert.ok(20 < scanned.length, `the scan found only ${scanned.length} scripts, so it is vacuous`);
  assert.ok(
    scanned.includes("scripts/unity/run-unity-docker.sh"),
    "the scan must reach the Docker wrapper, the script that mounts the cache"
  );
  assert.deepEqual(
    offenders,
    [],
    `these scripts restate the cache default instead of sourcing ${libraryRelativePath}: ` +
      offenders.join(", ")
  );
});

runTest("the workflow scanner still finds the paths this repository uploads", () => {
  // Every assertion below is vacuous if the scanner stops matching, so pin subjects it must see.
  const uploaded = uploadedPaths();
  assert.ok(
    20 <= uploaded.length,
    `scanner: expected the workflow path values to be found, saw ${uploaded.length}`
  );
  const directories = uploadedDirectories();
  for (const expected of [
    ".artifacts",
    ".artifacts/unity",
    ".artifacts/unity/unitypackage-project",
    ".artifacts/unity/unitypackage-smoke-project"
  ]) {
    assert.ok(
      directories.includes(expected),
      `scanner: ${expected} was not among the uploaded directories`
    );
  }
});

runTest("the Unity project directories inside the artifacts tree are still found", () => {
  const projects = unityProjectDirectories();
  const inArtifacts = [...projects.keys()].filter((project) => project.startsWith(".artifacts/"));
  assert.ok(
    2 <= inArtifacts.length,
    `expected the export project directories to be found, saw ${JSON.stringify([...projects])}`
  );
  for (const expected of [
    ".artifacts/unity/unitypackage-project",
    ".artifacts/unity/unitypackage-smoke-project"
  ]) {
    assert.ok(
      projects.has(expected),
      `${expected} was not discovered from the scripts or workflows`
    );
  }
});

runTest("no uploaded artifacts directory contains a license cache", () => {
  // The assertion the defect would have failed. With the cache defaulting into the Unity project
  // directory, the release export resolved to
  // .artifacts/unity/unitypackage-project/.unity-license-cache, which is inside a directory the
  // release job uploads four paths out of.
  const directories = uploadedDirectories().map((relative) => path.join(repoRoot, relative));
  const projects = unityProjectDirectories();
  const violations = [];
  for (const [project, source] of projects) {
    const absolute = path.isAbsolute(project) ? project : path.join(repoRoot, project);
    const cache = resolvedCacheDirectory(absolute, { RUNNER_TEMP: os.tmpdir() });
    for (const directory of directories) {
      if (isUnder(cache, directory)) {
        violations.push(`${project} (${source}) -> ${cache} is inside ${directory}`);
      }
    }
  }
  assert.ok(0 < projects.size, "no Unity project directory was resolved, so this proved nothing");
  assert.deepEqual(
    violations,
    [],
    "these license caches live inside a directory CI uploads from: " + violations.join("; ")
  );
});

runTest("no license cache resolves inside the artifacts tree at all", () => {
  const projects = unityProjectDirectories();
  const violations = [];
  for (const [project, source] of projects) {
    const absolute = path.isAbsolute(project) ? project : path.join(repoRoot, project);
    for (const environment of [{ RUNNER_TEMP: os.tmpdir() }, { RUNNER_TEMP: "" }]) {
      const cache = resolvedCacheDirectory(absolute, environment);
      if (isUnder(cache, artifactsRoot)) {
        violations.push(`${project} (${source}) -> ${cache}`);
      }
    }
  }
  assert.ok(0 < projects.size, "no Unity project directory was resolved, so this proved nothing");
  assert.deepEqual(
    violations,
    [],
    "these license caches resolve inside .artifacts, with and without RUNNER_TEMP: " +
      violations.join("; ")
  );
});

runTest("the devcontainer default still lives in the test-project volume", () => {
  // The reason the cache sits in the project volume at all: it is outside the checkout there, so it
  // carries no workspace ownership or commit risk, and it survives container restarts. Moving the
  // CI case must not move this one.
  const projectDirectory = devcontainerProjectDirectory();
  assert.ok(
    !isUnder(projectDirectory, repoRoot),
    `the devcontainer project directory must stay outside the checkout: ${projectDirectory}`
  );
  assert.strictEqual(
    resolvedCacheDirectory(projectDirectory, { RUNNER_TEMP: os.tmpdir() }),
    `${projectDirectory}/.unity-license-cache`,
    "the devcontainer cache must stay in the persistent test-project volume"
  );
});

runTest("an explicit cache directory inside the artifacts tree is refused", () => {
  const requested = path.join(artifactsRoot, "unity", "explicit-override-probe");
  const result = resolveCacheDirectory("/home/vscode/.unity-test-project", requested, {});
  assert.notStrictEqual(result.status, 0, "an in-artifacts override must fail rather than be used");
  assert.ok(
    result.stderr.includes(requested),
    `the refusal must name the rejected directory: ${result.stderr}`
  );
  assert.ok(
    result.stderr.includes("UNITY_LICENSE_CACHE_DIR"),
    `the refusal must say how to fix it: ${result.stderr}`
  );
});

runTest("the Docker wrapper mounts a cache outside the artifacts tree", () => {
  // The library being right is not the same as the entry point using it. This runs the real
  // wrapper against the real .artifacts export layout, with a stub docker so nothing is pulled, and
  // reads back the cache it announces and the volumes it asked for.
  const sandbox = fs.mkdtempSync(path.join(os.tmpdir(), "license-cache-probe-"));
  const projectDirectory = path.join(artifactsRoot, "unity", `license-cache-probe-${process.pid}`);
  try {
    const stubDirectory = path.join(sandbox, "bin");
    fs.mkdirSync(stubDirectory, { recursive: true });
    const stub = path.join(stubDirectory, "docker");
    fs.writeFileSync(stub, '#!/usr/bin/env bash\necho "stub docker $*"\nexit 1\n');
    fs.chmodSync(stub, 0o755);

    const result = spawnSync(
      "bash",
      [path.join(repoRoot, "scripts", "unity", "run-unity-docker.sh"), "-batchmode", "-quit"],
      {
        cwd: repoRoot,
        encoding: "utf8",
        timeout: 120000,
        env: {
          ...process.env,
          PATH: `${stubDirectory}${path.delimiter}${process.env.PATH}`,
          UNITY_TEST_PROJECT_DIR: projectDirectory,
          RUNNER_TEMP: path.join(sandbox, "runner-temp")
        }
      }
    );
    const output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
    const announced = /==> \[run-unity-docker\] Unity cache: (.+)/.exec(output);
    assert.ok(
      announced,
      `the wrapper did not announce a cache directory: ${output.slice(0, 2000)}`
    );
    assert.ok(
      !isUnder(announced[1].trim(), artifactsRoot),
      `the wrapper announced a cache inside the artifacts tree: ${announced[1].trim()}`
    );

    const mounts = [...output.matchAll(/-v (\S+):\/root\/\.(?:local\/share|config)\/unity3d/g)].map(
      (match) => match[1]
    );
    assert.strictEqual(mounts.length, 2, `expected both cache mounts, saw ${mounts.join(", ")}`);
    for (const mount of mounts) {
      assert.ok(
        !isUnder(mount, artifactsRoot),
        `the wrapper bind-mounted a cache inside the artifacts tree: ${mount}`
      );
    }
    assert.ok(
      !fs.existsSync(path.join(projectDirectory, ".unity-license-cache")),
      "the wrapper still created a cache directory inside the export project"
    );
  } finally {
    fs.rmSync(sandbox, { recursive: true, force: true });
    fs.rmSync(projectDirectory, { recursive: true, force: true });
    // Leave .artifacts as the run found it. rmdir only succeeds on an empty directory, so a real
    // export sitting alongside this probe is never touched.
    for (const created of [path.dirname(projectDirectory), artifactsRoot]) {
      try {
        fs.rmdirSync(created);
      } catch {
        break;
      }
    }
  }
});

console.log("");
console.log(`Tests passed: ${passed}`);
console.log(`Tests failed: ${failed}`);
if (0 < failedTests.length) {
  console.log("Failed tests:");
  for (const name of failedTests) {
    console.log(`  - ${name}`);
  }
}

process.exit(failed === 0 ? 0 : 1);
