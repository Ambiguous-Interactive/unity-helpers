"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { changedFiles, select } = require("../agent-check.js");

const TEST_MODES = Object.freeze(["editmode", "playmode", "standalone"]);

function resolveTestMatrix(
  versions,
  requestedVersion = "",
  requestedMode = "all",
  requestedAcceptance = "none"
) {
  if (
    !Array.isArray(versions) ||
    versions.length === 0 ||
    versions.some(
      (version) => typeof version !== "string" || version.trim() !== version || !version
    ) ||
    new Set(versions).size !== versions.length
  ) {
    throw new Error("Unity versions must be a nonempty list of distinct version strings.");
  }
  const version = requestedVersion.trim();
  const mode = requestedMode.trim();
  if (version && !versions.includes(version)) {
    throw new Error(`Unsupported Unity version: ${version}`);
  }
  if (mode && mode !== "all" && !TEST_MODES.includes(mode)) {
    throw new Error(`Unsupported Unity test mode: ${mode}`);
  }
  const acceptance = requestedAcceptance.trim();
  if (
    ["nullableconsumer", "wproto"].includes(acceptance) &&
    version &&
    version !== "2021.3.45f1" &&
    version !== "6000.6.0f1"
  ) {
    throw new Error(
      `${acceptance === "wproto" ? "WProto corpus" : "Nullable consumer"} acceptance requires its pinned floor or latest Unity version: ${version}`
    );
  }
  const acceptanceRequested = acceptance !== "" && acceptance !== "none";
  const selectedVersions = version ? [version] : [...versions];
  const selectedModes = !mode || mode === "all" ? [...TEST_MODES] : [mode];
  if (acceptanceRequested && !selectedModes.includes("standalone")) {
    // Native acceptance builds IL2CPP standalone players (intmap, serialization),
    // so standalone coverage must be selected whenever acceptance runs.
    selectedModes.push("standalone");
  }
  return {
    "unity-versions": selectedVersions,
    "unity-exclusions": versions
      .filter((candidate) => !selectedVersions.includes(candidate))
      .map((candidate) => ({ "unity-version": candidate })),
    "test-modes": selectedModes
  };
}

function resolvePullRequestMatrix(versions, files) {
  const full = resolveTestMatrix(versions);
  const proposal = files.length ? select(files, "pr").unity : "full";
  if (proposal === "full")
    return { ...full, coverage: "full", reason: "compatibility-sensitive or unknown input" };
  return {
    ...resolveTestMatrix(versions, versions[versions.length - 1]),
    "test-modes": ["editmode", "playmode"],
    coverage: "latest-editors",
    reason:
      proposal === "none"
        ? "documentation retains latest editor validation pending conditional enrollment"
        : "ordinary C#; latest editors, full compatibility on main and default dispatch"
  };
}

function main() {
  const repoRoot = path.resolve(__dirname, "..", "..");
  const versions = JSON.parse(
    fs.readFileSync(path.join(repoRoot, ".github", "unity-versions.json"), "utf8")
  ).all;
  let matrix = resolveTestMatrix(
    versions,
    process.env.INPUT_UNITY_VERSION || "",
    process.env.INPUT_TEST_MODE || "all",
    process.env.INPUT_ACCEPTANCE || "none"
  );
  if (process.env.GITHUB_EVENT_NAME === "pull_request") {
    if (!/^[0-9a-f]{40}$/.test(process.env.AGENT_CHECK_BASE || ""))
      throw new Error("PR matrix selection requires the immutable PR base SHA");
    matrix = resolvePullRequestMatrix(
      versions,
      changedFiles(process.env.AGENT_CHECK_BASE, repoRoot)
    );
  }
  if (!process.env.GITHUB_OUTPUT) {
    throw new Error("GITHUB_OUTPUT is required to publish the Unity test matrix.");
  }
  const outputs = Object.entries(matrix).map(
    ([key, value]) => `${key}=${typeof value === "string" ? value : JSON.stringify(value)}`
  );
  fs.appendFileSync(process.env.GITHUB_OUTPUT, `${outputs.join("\n")}\n`, "utf8");
  if (process.env.GITHUB_STEP_SUMMARY)
    fs.appendFileSync(
      process.env.GITHUB_STEP_SUMMARY,
      `### Selected Unity coverage\n\n${JSON.stringify(matrix)}\n\n` +
        "SINGLE_THREADED follows the selected modes. Package export smoke also runs. " +
        "Unselected versions and modes are not validated by this run. Main retains the full matrix.\n",
      "utf8"
    );
}

if (require.main === module) {
  try {
    main();
  } catch (error) {
    console.error(`[resolve-test-matrix] ${error.message}`);
    process.exitCode = 1;
  }
}

module.exports = { TEST_MODES, resolveTestMatrix, resolvePullRequestMatrix };
